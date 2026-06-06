import { useEffect, useMemo, useReducer, useRef, useState, type MouseEvent } from 'react';
import { createPortal } from 'react-dom';
import { CalendarDays, ChevronLeft, ChevronRight, FileSpreadsheet, Loader2, MoreHorizontal, Pencil, Plus, Save, Trash2, Users } from 'lucide-react';
import { useAuth } from '../../../../providers/AuthContext';
import { Checkbox } from '../../../../components/forms/Checkbox';
import { MultiSelectDropdown } from '../../../../components/forms/MultiSelectDropdown';
import type { WorksheetResponse } from '../../../../api/generated/models';
import type { AssignableUser } from '../../types';
import { getUserList } from '../../utils';
import { useGetApiUsers } from '../../../../api/generated/users/users';

const NUMBER_FORMATTER = new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 });
const DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: '2-digit', month: '2-digit', year: 'numeric' });
const MONTH_FORMATTER = new Intl.DateTimeFormat('da-DK', { month: 'long', year: 'numeric' });

type WorksheetDraft = {
  userId: string;
  workDate: string;
  hours: string;
  sleptOnJob: boolean;
};

type ActionMenuState = {
  worksheetId: string;
  top: number;
  right: number;
};

type UserOption = { id: string; label: string; description?: string };

type WorksheetUiState = {
  addDraft: WorksheetDraft;
  editDraft: WorksheetDraft | null;
  editingWorksheetId: string | null;
  openActionMenu: ActionMenuState | null;
  isAddOpen: boolean;
  formError: string | null;
};

type WorksheetUiAction =
  | { type: 'missingEditingWorksheet' }
  | { type: 'closeActionMenu' }
  | { type: 'toggleActionMenu'; worksheetId: string; top: number; right: number }
  | { type: 'setFormError'; error: string | null }
  | { type: 'setAddDraft'; draft: WorksheetDraft }
  | { type: 'setEditDraft'; draft: WorksheetDraft | null }
  | { type: 'openAdd'; defaultUserId: string }
  | { type: 'cancelAdd'; defaultUserId: string }
  | { type: 'toggleEdit'; worksheetId: string; draft: WorksheetDraft }
  | { type: 'cancelEdit' }
  | { type: 'deleteStarted'; worksheetId: string }
  | { type: 'saveSucceeded'; worksheetId?: string; defaultUserId: string };

type JobWorksheetsStepProps = {
  jobId: string;
  worksheets: WorksheetResponse[];
  totalHours: number | string | null;
  totalOutlay: number | string | null;
  assignableUsers: AssignableUser[];
  isLoadingUsers: boolean;
  isSaving: boolean;
  isDeleting: boolean;
  onUpsert: (params: { id?: string; jobId: string; userId: string; workDate: string; hoursWorked: number; sleptOnJob: boolean }) => Promise<unknown>;
  onDelete: (params: { worksheetId: string; jobId: string }) => void;
};

function todayIso(): string {
  const now = new Date();
  return toDateIso(now);
}

function toDateIso(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function fromDateIso(value: string): Date {
  const [year, month, day] = dateKey(value).split('-').map(Number);
  return new Date(year, month - 1, day);
}

function dateKey(value: string): string {
  return value.slice(0, 10);
}

function parseHours(value: number | string): number {
  return typeof value === 'number' ? value : Number(value.replace(',', '.'));
}

function parseNullableNumber(value: number | string | null): number {
  if (value === null) return 0;
  const parsedValue = typeof value === 'number' ? value : Number(value.replace(',', '.'));
  return Number.isFinite(parsedValue) ? parsedValue : 0;
}

function formatNumber(value: number): string {
  return NUMBER_FORMATTER.format(value);
}

function formatUnit(value: number, singular: string, plural: string): string {
  return Math.abs(value) === 1 ? singular : plural;
}

function formatDate(value: string): string {
  return DATE_FORMATTER.format(fromDateIso(value));
}

function defaultDraft(defaultUserId: string): WorksheetDraft {
  return {
    userId: defaultUserId,
    workDate: todayIso(),
    hours: '',
    sleptOnJob: false,
  };
}

function initialWorksheetUiState(defaultUserId: string): WorksheetUiState {
  return {
    addDraft: defaultDraft(defaultUserId),
    editDraft: null,
    editingWorksheetId: null,
    openActionMenu: null,
    isAddOpen: false,
    formError: null,
  };
}

function worksheetUiReducer(state: WorksheetUiState, action: WorksheetUiAction): WorksheetUiState {
  switch (action.type) {
    case 'missingEditingWorksheet':
      return { ...state, editingWorksheetId: null, editDraft: null, formError: null };
    case 'closeActionMenu':
      return { ...state, openActionMenu: null };
    case 'toggleActionMenu':
      return {
        ...state,
        openActionMenu: state.openActionMenu?.worksheetId === action.worksheetId
          ? null
          : { worksheetId: action.worksheetId, top: action.top, right: action.right },
      };
    case 'setFormError':
      return { ...state, formError: action.error };
    case 'setAddDraft':
      return { ...state, addDraft: action.draft };
    case 'setEditDraft':
      return { ...state, editDraft: action.draft };
    case 'openAdd':
      return {
        ...state,
        editingWorksheetId: null,
        editDraft: null,
        openActionMenu: null,
        addDraft: state.addDraft.userId || !action.defaultUserId
          ? state.addDraft
          : { ...state.addDraft, userId: action.defaultUserId },
        isAddOpen: true,
        formError: null,
      };
    case 'cancelAdd':
      return { ...state, addDraft: defaultDraft(action.defaultUserId), isAddOpen: false, formError: null };
    case 'toggleEdit':
      if (state.editingWorksheetId === action.worksheetId) {
        return { ...state, editingWorksheetId: null, editDraft: null, openActionMenu: null, formError: null };
      }
      return {
        ...state,
        editingWorksheetId: action.worksheetId,
        editDraft: action.draft,
        openActionMenu: null,
        isAddOpen: false,
        formError: null,
      };
    case 'cancelEdit':
      return { ...state, editingWorksheetId: null, editDraft: null, formError: null };
    case 'deleteStarted':
      if (state.editingWorksheetId !== action.worksheetId) {
        return { ...state, openActionMenu: null };
      }
      return { ...state, editingWorksheetId: null, editDraft: null, openActionMenu: null, formError: null };
    case 'saveSucceeded':
      if (action.worksheetId) {
        return { ...state, editingWorksheetId: null, editDraft: null, formError: null };
      }
      return {
        ...state,
        addDraft: defaultDraft(action.defaultUserId),
        isAddOpen: false,
        formError: null,
      };
    default:
      return state;
  }
}

export function JobWorksheetsStep({
  jobId,
  worksheets,
  totalHours,
  totalOutlay,
  assignableUsers,
  isLoadingUsers,
  isSaving,
  isDeleting,
  onUpsert,
  onDelete,
}: JobWorksheetsStepProps) {
  const { user } = useAuth();
  const usersQuery = useGetApiUsers();
  const resolvedUsers = assignableUsers.length > 0 ? assignableUsers : getUserList(usersQuery.data);
  const defaultUserId = user?.email ? (resolvedUsers.find((u) => u.email === user.email)?.id ?? '') : '';
  const userOptions = resolvedUsers.map((u) => ({ id: u.id, label: u.displayName, description: u.email }));

  const [uiState, dispatch] = useReducer(worksheetUiReducer, defaultUserId, initialWorksheetUiState);
  const { addDraft, editDraft, editingWorksheetId, openActionMenu, isAddOpen, formError } = uiState;

  const sortedWorksheets = useMemo(
    () => [...worksheets].sort((a, b) => b.workDate.localeCompare(a.workDate)),
    [worksheets],
  );
  const totalHoursValue = parseNullableNumber(totalHours);
  const totalOutlayValue = parseNullableNumber(totalOutlay);
  const openActionWorksheet = openActionMenu
    ? sortedWorksheets.find((worksheet) => worksheet.id === openActionMenu.worksheetId) ?? null
    : null;

  useEffect(() => {
    if (!editingWorksheetId) return;
    if (worksheets.some((worksheet) => worksheet.id === editingWorksheetId)) return;

    dispatch({ type: 'missingEditingWorksheet' });
  }, [editingWorksheetId, worksheets]);

  useEffect(() => {
    if (!openActionMenu) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (event.target instanceof Element && event.target.closest('.worksheet-actions-menu-root, .worksheet-actions-menu')) return;
      dispatch({ type: 'closeActionMenu' });
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [openActionMenu]);

  useEffect(() => {
    if (!openActionMenu) return;

    const closeMenu = () => dispatch({ type: 'closeActionMenu' });
    const scrollContainer = document.querySelector('.app-content');
    scrollContainer?.addEventListener('scroll', closeMenu, { passive: true });
    window.addEventListener('resize', closeMenu);

    return () => {
      scrollContainer?.removeEventListener('scroll', closeMenu);
      window.removeEventListener('resize', closeMenu);
    };
  }, [openActionMenu]);

  const validateDraft = (draft: WorksheetDraft, currentWorksheetId?: string): number | null => {
    if (!draft.userId) {
      dispatch({ type: 'setFormError', error: 'Vælg en montør.' });
      return null;
    }

    const hoursNumber = Number(draft.hours.replace(',', '.'));
    if (!Number.isFinite(hoursNumber) || hoursNumber <= 0) {
      dispatch({ type: 'setFormError', error: 'Timer skal være større end 0' });
      return null;
    }

    if (hoursNumber > 24) {
      dispatch({ type: 'setFormError', error: 'Timer kan ikke overstige 24 på en dag.' });
      return null;
    }

    if (!Number.isInteger(hoursNumber * 4)) {
      dispatch({ type: 'setFormError', error: 'Timer skal angives i intervaller af 0,25.' });
      return null;
    }

    const existingTotal = worksheets
      .filter((worksheet) => worksheet.id !== currentWorksheetId)
      .filter((worksheet) => worksheet.userId === draft.userId && dateKey(worksheet.workDate) === dateKey(draft.workDate))
      .reduce((total, worksheet) => total + parseHours(worksheet.hoursWorked), 0);

    if (!Number.isFinite(existingTotal) || existingTotal + hoursNumber > 24) {
      dispatch({ type: 'setFormError', error: 'Montøren kan ikke registrere mere end 24 timer på samme dato.' });
      return null;
    }

    return hoursNumber;
  };

  const saveDraft = async (draft: WorksheetDraft, worksheetId?: string) => {
    dispatch({ type: 'setFormError', error: null });
    const hoursWorked = validateDraft(draft, worksheetId);
    if (hoursWorked === null) return;

    try {
      await onUpsert({
        id: worksheetId,
        jobId,
        userId: draft.userId,
        workDate: dateKey(draft.workDate),
        hoursWorked,
        sleptOnJob: draft.sleptOnJob,
      });
    } catch {
      return;
    }

    dispatch({ type: 'saveSucceeded', worksheetId, defaultUserId });
  };

  const startEdit = (worksheet: WorksheetResponse) => {
    dispatch({
      type: 'toggleEdit',
      worksheetId: worksheet.id,
      draft: {
        userId: worksheet.userId,
        workDate: dateKey(worksheet.workDate),
        hours: String(parseHours(worksheet.hoursWorked)),
        sleptOnJob: worksheet.sleptOnJob,
      },
    });
  };

  const handleDelete = (worksheet: WorksheetResponse) => {
    dispatch({ type: 'deleteStarted', worksheetId: worksheet.id });
    const confirmed = window.confirm('Slet denne timeseddel?');
    if (!confirmed) return;
    onDelete({ worksheetId: worksheet.id, jobId });
  };

  const toggleActionMenu = (event: MouseEvent<HTMLButtonElement>, worksheetId: string) => {
    const rect = event.currentTarget.getBoundingClientRect();
    dispatch({
      type: 'toggleActionMenu',
      worksheetId,
      top: rect.bottom + 6,
      right: window.innerWidth - rect.right,
    });
  };

  return (
    <>
      <WorksheetsSection
        sortedWorksheets={sortedWorksheets}
        resolvedUsers={resolvedUsers}
        userOptions={userOptions}
        addDraft={addDraft}
        editDraft={editDraft}
        editingWorksheetId={editingWorksheetId}
        openActionMenu={openActionMenu}
        isAddOpen={isAddOpen}
        isLoadingUsers={isLoadingUsers}
        isSaving={isSaving}
        formError={formError}
        onToggleActionMenu={toggleActionMenu}
        onEditDraftChange={(draft) => dispatch({ type: 'setEditDraft', draft })}
        onSaveEdit={(draft, worksheetId) => saveDraft(draft, worksheetId)}
        onCancelEdit={() => dispatch({ type: 'cancelEdit' })}
        onOpenAddForm={() => dispatch({ type: 'openAdd', defaultUserId })}
        onAddDraftChange={(draft) => dispatch({ type: 'setAddDraft', draft })}
        onSaveAdd={(draft) => saveDraft(draft)}
        onCancelAdd={() => dispatch({ type: 'cancelAdd', defaultUserId })}
      />

      <WorksheetTotalsSection totalHoursValue={totalHoursValue} totalOutlayValue={totalOutlayValue} />

      <WorksheetActionMenuPortal
        openActionMenu={openActionMenu}
        openActionWorksheet={openActionWorksheet}
        isDeleting={isDeleting}
        onStartEdit={startEdit}
        onDelete={handleDelete}
      />
    </>
  );
}

type WorksheetsSectionProps = {
  sortedWorksheets: WorksheetResponse[];
  resolvedUsers: AssignableUser[];
  userOptions: UserOption[];
  addDraft: WorksheetDraft;
  editDraft: WorksheetDraft | null;
  editingWorksheetId: string | null;
  openActionMenu: ActionMenuState | null;
  isAddOpen: boolean;
  isLoadingUsers: boolean;
  isSaving: boolean;
  formError: string | null;
  onToggleActionMenu: (event: MouseEvent<HTMLButtonElement>, worksheetId: string) => void;
  onEditDraftChange: (draft: WorksheetDraft) => void;
  onSaveEdit: (draft: WorksheetDraft, worksheetId: string) => void;
  onCancelEdit: () => void;
  onOpenAddForm: () => void;
  onAddDraftChange: (draft: WorksheetDraft) => void;
  onSaveAdd: (draft: WorksheetDraft) => void;
  onCancelAdd: () => void;
};

function WorksheetsSection({
  sortedWorksheets,
  resolvedUsers,
  userOptions,
  addDraft,
  editDraft,
  editingWorksheetId,
  openActionMenu,
  isAddOpen,
  isLoadingUsers,
  isSaving,
  formError,
  onToggleActionMenu,
  onEditDraftChange,
  onSaveEdit,
  onCancelEdit,
  onOpenAddForm,
  onAddDraftChange,
  onSaveAdd,
  onCancelAdd,
}: WorksheetsSectionProps) {
  return (
    <section className="detail-section">
      <div className="section-header-row">
        <FileSpreadsheet size={18} />
        <h3>Timesedler</h3>
      </div>

      <WorksheetList
        sortedWorksheets={sortedWorksheets}
        resolvedUsers={resolvedUsers}
        userOptions={userOptions}
        editDraft={editDraft}
        editingWorksheetId={editingWorksheetId}
        openActionMenu={openActionMenu}
        isLoadingUsers={isLoadingUsers}
        isSaving={isSaving}
        formError={formError}
        onToggleActionMenu={onToggleActionMenu}
        onEditDraftChange={onEditDraftChange}
        onSaveEdit={onSaveEdit}
        onCancelEdit={onCancelEdit}
      />

      {(!editingWorksheetId || sortedWorksheets.length === 0) && !isAddOpen && (
        <button type="button" className="btn btn-primary worksheet-add-trigger" onClick={onOpenAddForm}>
          <Plus size={16} />
          <span>Tilføj timeseddel</span>
        </button>
      )}

      {!editingWorksheetId && isAddOpen && (
        <WorksheetDraftForm
          title="Tilføj timeseddel"
          draft={addDraft}
          userOptions={userOptions}
          isLoadingUsers={isLoadingUsers}
          isSaving={isSaving}
          submitLabel="Tilføj"
          error={formError}
          onDraftChange={onAddDraftChange}
          onSubmit={() => onSaveAdd(addDraft)}
          onCancel={onCancelAdd}
        />
      )}
    </section>
  );
}

type WorksheetListProps = {
  sortedWorksheets: WorksheetResponse[];
  resolvedUsers: AssignableUser[];
  userOptions: UserOption[];
  editDraft: WorksheetDraft | null;
  editingWorksheetId: string | null;
  openActionMenu: ActionMenuState | null;
  isLoadingUsers: boolean;
  isSaving: boolean;
  formError: string | null;
  onToggleActionMenu: (event: MouseEvent<HTMLButtonElement>, worksheetId: string) => void;
  onEditDraftChange: (draft: WorksheetDraft) => void;
  onSaveEdit: (draft: WorksheetDraft, worksheetId: string) => void;
  onCancelEdit: () => void;
};

function WorksheetList({
  sortedWorksheets,
  resolvedUsers,
  userOptions,
  editDraft,
  editingWorksheetId,
  openActionMenu,
  isLoadingUsers,
  isSaving,
  formError,
  onToggleActionMenu,
  onEditDraftChange,
  onSaveEdit,
  onCancelEdit,
}: WorksheetListProps) {
  if (sortedWorksheets.length === 0) {
    return <p className="empty-state-text">Ingen timesedler endnu.</p>;
  }

  return (
    <ul className={editingWorksheetId ? 'worksheet-list expanded' : 'worksheet-list'}>
      {sortedWorksheets.map((worksheet) => {
        const assignee = resolvedUsers.find((u) => u.id === worksheet.userId);
        const isEditing = editingWorksheetId === worksheet.id && editDraft;

        return (
          <li key={worksheet.id} className={isEditing ? 'worksheet-list-item editing' : 'worksheet-list-item'}>
            <div className="worksheet-list-item-row">
              <div className="worksheet-list-item-info">
                <span className="worksheet-list-item-date">{formatDate(worksheet.workDate)}</span>
                <span className="worksheet-list-item-meta">{assignee?.displayName ?? worksheet.userId}</span>
              </div>
              <div className="worksheet-list-item-hours" aria-label={`${formatNumber(parseHours(worksheet.hoursWorked))} timer`}>
                <strong>{formatNumber(parseHours(worksheet.hoursWorked))}</strong>
                <span>{formatUnit(parseHours(worksheet.hoursWorked), 'time', 'timer')}</span>
              </div>
              {worksheet.sleptOnJob && <span className="worksheet-list-item-outlay">Udlæg</span>}
              <div className="worksheet-list-item-actions">
                <div className="worksheet-actions-menu-root">
                  <button
                    type="button"
                    className="btn-icon"
                    onClick={(event) => onToggleActionMenu(event, worksheet.id)}
                    aria-label="Åbn handlinger for timeseddel"
                    aria-expanded={openActionMenu?.worksheetId === worksheet.id}
                    title="Handlinger"
                  >
                    <MoreHorizontal size={18} />
                  </button>
                </div>
              </div>
            </div>

            {isEditing && (
              <WorksheetDraftForm
                title="Rediger timeseddel"
                draft={editDraft}
                userOptions={userOptions}
                isLoadingUsers={isLoadingUsers}
                isSaving={isSaving}
                submitLabel="Gem"
                error={formError}
                onDraftChange={onEditDraftChange}
                onSubmit={() => onSaveEdit(editDraft, worksheet.id)}
                onCancel={onCancelEdit}
              />
            )}
          </li>
        );
      })}
    </ul>
  );
}

function WorksheetTotalsSection({ totalHoursValue, totalOutlayValue }: { totalHoursValue: number; totalOutlayValue: number }) {
  return (
    <section className="detail-section worksheet-total-section" aria-label="Timeseddel totaler">
      <div className="worksheet-total-row">
        <span className="worksheet-total-label">Timer i alt:</span>
        <strong>{formatNumber(totalHoursValue)} {formatUnit(totalHoursValue, 'time', 'timer')}</strong>
      </div>
      <div className="worksheet-total-row">
        <span className="worksheet-total-label">Udlæg:</span>
        <strong>{formatNumber(totalOutlayValue)} {formatUnit(totalOutlayValue, 'dag', 'dage')}</strong>
      </div>
    </section>
  );
}

type WorksheetActionMenuPortalProps = {
  openActionMenu: ActionMenuState | null;
  openActionWorksheet: WorksheetResponse | null;
  isDeleting: boolean;
  onStartEdit: (worksheet: WorksheetResponse) => void;
  onDelete: (worksheet: WorksheetResponse) => void;
};

function WorksheetActionMenuPortal({
  openActionMenu,
  openActionWorksheet,
  isDeleting,
  onStartEdit,
  onDelete,
}: WorksheetActionMenuPortalProps) {
  if (!openActionMenu || !openActionWorksheet) return null;

  return createPortal(
    <div
      className="worksheet-actions-menu"
      role="menu"
      style={{ top: openActionMenu.top, right: openActionMenu.right }}
    >
      <button type="button" role="menuitem" onClick={() => onStartEdit(openActionWorksheet)}>
        <Pencil size={15} />
        <span>Rediger</span>
      </button>
      <button
        type="button"
        className="danger"
        role="menuitem"
        onClick={() => onDelete(openActionWorksheet)}
        disabled={isDeleting}
      >
        <Trash2 size={15} />
        <span>Slet</span>
      </button>
    </div>,
    document.body,
  );
}

type WorksheetDraftFormProps = {
  title: string;
  draft: WorksheetDraft;
  userOptions: UserOption[];
  isLoadingUsers: boolean;
  isSaving: boolean;
  submitLabel: string;
  error?: string | null;
  onDraftChange: (draft: WorksheetDraft) => void;
  onSubmit: () => void;
  onCancel?: () => void;
};

function WorksheetDraftForm({
  title,
  draft,
  userOptions,
  isLoadingUsers,
  isSaving,
  submitLabel,
  error,
  onDraftChange,
  onSubmit,
  onCancel,
}: WorksheetDraftFormProps) {
  const updateDraft = (patch: Partial<WorksheetDraft>) => onDraftChange({ ...draft, ...patch });

  return (
    <div className="worksheet-form">
      <h4>{title}</h4>
      <div className="worksheet-form-grid worksheet-form-grid-main">
        <CalendarPicker value={draft.workDate} onChange={(workDate) => updateDraft({ workDate })} />

        <MultiSelectDropdown
          label="Montør"
          placeholder="Vælg montør"
          emptyText="Ingen medarbejdere fundet"
          loadingText="Henter medarbejdere..."
          options={userOptions}
          selectedIds={draft.userId ? [draft.userId] : []}
          isLoading={isLoadingUsers}
          icon={<Users size={16} />}
          onChange={(ids) => updateDraft({ userId: ids.at(-1) ?? '' })}
        />
      </div>

      <div className="worksheet-form-grid worksheet-form-grid-hours">
        <div className="form-group">
          <label className="form-label" htmlFor={`${title}-worksheet-hours`}>Timer</label>
          <input
            id={`${title}-worksheet-hours`}
            className="form-input"
            type="number"
            min="0"
            max="24"
            step="0.25"
            inputMode="decimal"
            value={draft.hours}
            onChange={(e) => updateDraft({ hours: e.target.value })}
            placeholder="0"
          />
        </div>

        <div className="worksheet-overnight-wrapper">
          <Checkbox
            checked={draft.sleptOnJob}
            onChange={() => updateDraft({ sleptOnJob: !draft.sleptOnJob })}
            label="Udlæg"
          />
        </div>
      </div>

      {error && <p className="form-error-text">{error}</p>}

      <div className="worksheet-form-actions">
        <button
          type="button"
          className="btn btn-primary"
          onClick={onSubmit}
          disabled={isSaving}
        >
          {isSaving && <Loader2 className="animate-spin" size={16} />}
          {!isSaving && submitLabel !== 'Tilføj' && <Save size={16} />}
          <span>{isSaving ? 'Gemmer...' : submitLabel}</span>
        </button>
        {onCancel && (
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onCancel}
            disabled={isSaving}
          >
            Annuller
          </button>
        )}
      </div>
    </div>
  );
}

function CalendarPicker({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const selectedDate = fromDateIso(value);
  const [isOpen, setIsOpen] = useState(false);
  const [visibleMonth, setVisibleMonth] = useState(() => new Date(selectedDate.getFullYear(), selectedDate.getMonth(), 1));
  const pickerRef = useRef<HTMLDivElement | null>(null);
  const monthLabel = MONTH_FORMATTER.format(visibleMonth);
  const firstDay = new Date(visibleMonth.getFullYear(), visibleMonth.getMonth(), 1);
  const startOffset = (firstDay.getDay() + 6) % 7;
  const daysInMonth = new Date(visibleMonth.getFullYear(), visibleMonth.getMonth() + 1, 0).getDate();
  const days = Array.from({ length: startOffset + daysInMonth }, (_, index) => index < startOffset ? null : index - startOffset + 1);

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [isOpen]);

  const moveMonth = (offset: number) => {
    setVisibleMonth((current) => new Date(current.getFullYear(), current.getMonth() + offset, 1));
  };

  const selectDay = (day: number) => {
    const nextDate = new Date(visibleMonth.getFullYear(), visibleMonth.getMonth(), day);
    onChange(toDateIso(nextDate));
    setIsOpen(false);
  };

  return (
    <div className="form-group calendar-picker-field" ref={pickerRef}>
      <label className="form-label">Dato</label>
      <button
        type="button"
        className="form-input calendar-picker-trigger"
        onClick={() => setIsOpen((open) => !open)}
        aria-expanded={isOpen}
      >
        <span>{formatDate(value)}</span>
        <CalendarDays size={16} />
      </button>

      {isOpen && (
        <div className="calendar-picker-popover">
          <div className="calendar-picker-header">
            <button type="button" className="btn-icon" onClick={() => moveMonth(-1)} aria-label="Forrige måned">
              <ChevronLeft size={16} />
            </button>
            <span>{monthLabel}</span>
            <button type="button" className="btn-icon" onClick={() => moveMonth(1)} aria-label="Næste måned">
              <ChevronRight size={16} />
            </button>
          </div>
          <div className="calendar-picker-weekdays">
            {['ma', 'ti', 'on', 'to', 'fr', 'lø', 'sø'].map((day) => <span key={day}>{day}</span>)}
          </div>
          <div className="calendar-picker-grid">
            {days.map((day, index) => {
              if (!day) return <span key={`blank-${index}`} />;
              const dayIso = toDateIso(new Date(visibleMonth.getFullYear(), visibleMonth.getMonth(), day));
              return (
                <button
                  key={dayIso}
                  type="button"
                  className={dayIso === value ? 'calendar-picker-day selected' : 'calendar-picker-day'}
                  onClick={() => selectDay(day)}
                >
                  {day}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
