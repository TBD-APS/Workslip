import { useEffect, useMemo, useRef, useState } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight, FileSpreadsheet, Loader2, Pencil, Plus, Save, Users } from 'lucide-react';
import { useAuth } from '../../../../providers/AuthContext';
import { Checkbox } from '../../../../components/forms/Checkbox';
import { MultiSelectDropdown } from '../../../../components/forms/MultiSelectDropdown';
import { DeleteButton } from '../../../../components/common/DeleteButton';
import type { WorksheetResponse } from '../../../../api/generated/models';
import type { AssignableUser } from '../../types';
import { getUserList } from '../../utils';
import { useGetApiUsers } from '../../../../api/generated/users/users';

type WorksheetDraft = {
  userId: string;
  workDate: string;
  hours: string;
  sleptOnJob: boolean;
};

type JobWorksheetsStepProps = {
  jobId: string;
  worksheets: WorksheetResponse[];
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

function formatDate(value: string): string {
  return new Intl.DateTimeFormat('da-DK', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(fromDateIso(value));
}

function defaultDraft(defaultUserId: string): WorksheetDraft {
  return {
    userId: defaultUserId,
    workDate: todayIso(),
    hours: '',
    sleptOnJob: false,
  };
}

export function JobWorksheetsStep({
  jobId,
  worksheets,
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
  const defaultUserId =
    user?.email ? (resolvedUsers.find((u) => u.email === user.email)?.id ?? '') : '';
  const userOptions = resolvedUsers.map((u) => ({ id: u.id, label: u.displayName, description: u.email }));

  const [addDraft, setAddDraft] = useState<WorksheetDraft>(() => defaultDraft(defaultUserId));
  const [editDraft, setEditDraft] = useState<WorksheetDraft | null>(null);
  const [editingWorksheetId, setEditingWorksheetId] = useState<string | null>(null);
  const [isAddOpen, setIsAddOpen] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const sortedWorksheets = useMemo(
    () => [...worksheets].sort((a, b) => b.workDate.localeCompare(a.workDate)),
    [worksheets],
  );

  useEffect(() => {
    if (!editingWorksheetId) return;
    if (worksheets.some((worksheet) => worksheet.id === editingWorksheetId)) return;

    setEditingWorksheetId(null);
    setEditDraft(null);
    setFormError(null);
  }, [editingWorksheetId, worksheets]);

  const validateDraft = (draft: WorksheetDraft, currentWorksheetId?: string): number | null => {
    if (!draft.userId) {
      setFormError('Vælg en montør.');
      return null;
    }

    const hoursNumber = Number(draft.hours.replace(',', '.'));
    if (!Number.isFinite(hoursNumber) || hoursNumber <= 0) {
      setFormError('Timer skal være større end 0');
      return null;
    }

    if (hoursNumber > 24) {
      setFormError('Timer kan ikke overstige 24 på en dag.');
      return null;
    }

    if (!Number.isInteger(hoursNumber * 4)) {
      setFormError('Timer skal angives i intervaller af 0,25.');
      return null;
    }

    const existingTotal = worksheets
      .filter((worksheet) => worksheet.id !== currentWorksheetId)
      .filter((worksheet) => worksheet.userId === draft.userId && dateKey(worksheet.workDate) === dateKey(draft.workDate))
      .reduce((total, worksheet) => total + parseHours(worksheet.hoursWorked), 0);

    if (!Number.isFinite(existingTotal) || existingTotal + hoursNumber > 24) {
      setFormError('Montøren kan ikke registrere mere end 24 timer på samme dato.');
      return null;
    }

    return hoursNumber;
  };

  const saveDraft = async (draft: WorksheetDraft, worksheetId?: string) => {
    setFormError(null);
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

    if (worksheetId) {
      setEditingWorksheetId(null);
      setEditDraft(null);
      return;
    }

    setAddDraft(defaultDraft(defaultUserId));
    setIsAddOpen(false);
  };

  const openAddForm = () => {
    setEditingWorksheetId(null);
    setEditDraft(null);
    setAddDraft((current) => current.userId || !defaultUserId ? current : { ...current, userId: defaultUserId });
    setIsAddOpen(true);
    setFormError(null);
  };

  const cancelAdd = () => {
    setAddDraft(defaultDraft(defaultUserId));
    setIsAddOpen(false);
    setFormError(null);
  };

  const startEdit = (worksheet: WorksheetResponse) => {
    if (editingWorksheetId === worksheet.id) {
      setEditingWorksheetId(null);
      setEditDraft(null);
      setFormError(null);
      return;
    }

    setEditingWorksheetId(worksheet.id);
    setIsAddOpen(false);
    setEditDraft({
      userId: worksheet.userId,
      workDate: dateKey(worksheet.workDate),
      hours: String(parseHours(worksheet.hoursWorked)),
      sleptOnJob: worksheet.sleptOnJob,
    });
    setFormError(null);
  };

  const cancelEdit = () => {
    setEditingWorksheetId(null);
    setEditDraft(null);
    setFormError(null);
  };

  const handleDelete = (worksheet: WorksheetResponse) => {
    const confirmed = window.confirm('Slet denne arbejdsseddel?');
    if (!confirmed) return;
    if (editingWorksheetId === worksheet.id) {
      setEditingWorksheetId(null);
      setEditDraft(null);
      setFormError(null);
    }
    onDelete({ worksheetId: worksheet.id, jobId });
  };

  return (
    <section className="detail-section">
      <div className="section-header-row">
        <FileSpreadsheet size={18} />
        <h3>Arbejdssedler</h3>
      </div>

      {sortedWorksheets.length === 0 ? (
        <p className="empty-state-text">Ingen arbejdssedler endnu. </p>
      ) : (
        <ul className={editingWorksheetId ? 'worksheet-list expanded' : 'worksheet-list'}>
          {sortedWorksheets.map((worksheet) => {
            const assignee = resolvedUsers.find((u) => u.id === worksheet.userId);
            const isEditing = editingWorksheetId === worksheet.id && editDraft;

            return (
              <li key={worksheet.id} className={isEditing ? 'worksheet-list-item editing' : 'worksheet-list-item'}>
                <div className="worksheet-list-item-row">
                  <div className="worksheet-list-item-info">
                    <span className="worksheet-list-item-date">{formatDate(worksheet.workDate)}</span>
                    <span className="worksheet-list-item-meta">
                      {assignee?.displayName ?? worksheet.userId}
                      {' · '}
                      {Number(worksheet.hoursWorked)} t
                      {worksheet.sleptOnJob ? ' · overnattet' : ''}
                    </span>
                  </div>
                  <div className="worksheet-list-item-actions">
                    <button
                      type="button"
                      className="btn-icon"
                      onClick={() => startEdit(worksheet)}
                      aria-label="Rediger arbejdsseddel"
                      title="Rediger"
                    >
                      <Pencil size={16} />
                    </button>
                    <DeleteButton
                      onClick={() => handleDelete(worksheet)}
                      disabled={isDeleting}
                      ariaLabel="Slet arbejdsseddel"
                      title="Slet arbejdsseddel"
                      size={16}
                    />
                  </div>
                </div>

                {isEditing && (
                  <WorksheetDraftForm
                    title="Rediger arbejdsseddel"
                    draft={editDraft}
                    userOptions={userOptions}
                    isLoadingUsers={isLoadingUsers}
                    isSaving={isSaving}
                    submitLabel="Gem"
                    error={formError}
                    onDraftChange={setEditDraft}
                    onSubmit={() => saveDraft(editDraft, worksheet.id)}
                    onCancel={cancelEdit}
                  />
                )}
              </li>
            );
          })}
        </ul>
      )}

      {(!editingWorksheetId || sortedWorksheets.length === 0) && !isAddOpen && (
        <button
          type="button"
          className="btn btn-primary worksheet-add-trigger"
          onClick={openAddForm}
        >
          <Plus size={16} />
          <span>Tilføj arbejdsseddel</span>
        </button>
      )}

      {!editingWorksheetId && isAddOpen && (
        <WorksheetDraftForm
          title="Tilføj arbejdsseddel"
          draft={addDraft}
          userOptions={userOptions}
          isLoadingUsers={isLoadingUsers}
          isSaving={isSaving}
          submitLabel="Tilføj"
          error={formError}
          onDraftChange={setAddDraft}
          onSubmit={() => saveDraft(addDraft)}
          onCancel={cancelAdd}
        />
      )}
    </section>
  );
}

type WorksheetDraftFormProps = {
  title: string;
  draft: WorksheetDraft;
  userOptions: Array<{ id: string; label: string; description?: string }>;
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
  const monthLabel = new Intl.DateTimeFormat('da-DK', { month: 'long', year: 'numeric' }).format(visibleMonth);
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
