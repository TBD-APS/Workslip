import { useEffect, useMemo, useReducer, useState, type MouseEvent } from 'react';
import { useAuth } from '../../../../providers/useAuth';
import { useCan } from '../../../../providers/permissions';
import type { UserViewModel, WorksheetResponse } from '../../../../api/generated/models';
import { useGetApiUsers } from '../../../../api/generated/users/users';
import { parseNullableNumber } from '../../../../lib/formatUtils';
import { WorksheetsSection } from '../../components/WorksheetsSection';
import { WorksheetActionMenuPortal } from '../../components/WorksheetActionMenuPortal';
import { ConfirmDeleteDialog } from '../../../../components/common/ConfirmDeleteDialog';
import { initialWorksheetUiState, worksheetUiReducer, dateKey, parseHours, validateWorksheetDraft } from '../../components/worksheetUtils';
import type { WorksheetDraft } from '../../components/worksheetUtils';

type BaseProps = {
  assignableUsers: UserViewModel[];
  isLoadingUsers: boolean;
  variant?: 'section' | 'list' | 'flat';
};

type ServerModeProps = BaseProps & {
  localMode?: false;
  jobId: string;
  worksheets: WorksheetResponse[];
  totalHours: number | string | null;
  totalOutlay: number | string | null;
  isSaving: boolean;
  isDeleting: boolean;
  onUpsert: (params: { id?: string; jobId: string; userId: string; userDisplayName: string; workDate: string; hoursWorked: number; sleptOnJob: boolean }) => Promise<unknown>;
  onDelete: (params: { worksheetId: string; jobId: string }) => void;
  onChange?: never;
};

type LocalModeProps = BaseProps & {
  localMode: true;
  jobId?: never;
  worksheets?: never;
  totalHours?: never;
  totalOutlay?: never;
  isSaving?: never;
  isDeleting?: never;
  onUpsert?: never;
  onDelete?: never;
  onChange: (worksheets: WorksheetDraft[]) => void;
};

type JobWorksheetsStepProps = ServerModeProps | LocalModeProps;

function draftToResponse(draft: WorksheetDraft): WorksheetResponse {
  return {
    id: draft.id ?? '',
    userId: draft.userId,
    workDate: draft.workDate,
    hoursWorked: String(draft.hours),
    sleptOnJob: draft.sleptOnJob,
  } as WorksheetResponse;
}

export function JobWorksheetsStep({
  assignableUsers,
  isLoadingUsers,
  variant = 'section',
  ...rest
}: JobWorksheetsStepProps) {
  const localMode = rest.localMode === true;
  const { user } = useAuth();
  const canPickUserServer = useCan('worksheet:assign');
  const canPickUser = localMode ? assignableUsers.length > 0 : canPickUserServer;
  useGetApiUsers({ limit: 20 }, { query: { enabled: canPickUser && !localMode } });
  const resolvedUsers = useMemo(
    () => (canPickUser
      ? (assignableUsers.length > 0 ? assignableUsers : null)
      : []) ?? [],
    [canPickUser, assignableUsers],
  );
  const defaultUserId = localMode
    ? (user?.id ?? '')
    : canPickUser
      ? (user?.email ? (resolvedUsers.find((u) => u.email === user.email)?.id ?? '') : '')
      : (user?.id ?? '');
  const userOptions = resolvedUsers.map((u) => ({ id: u.id, label: u.displayName }));
  const currentUserName = user?.displayName ?? user?.email ?? 'dig';

  const displayNameFor = (userId: string): string => {
    if (!canPickUser) return currentUserName;
    return resolvedUsers.find((u) => u.id === userId)?.displayName ?? userId.slice(0, 8);
  };

  const [uiState, dispatch] = useReducer(worksheetUiReducer, defaultUserId, initialWorksheetUiState);
  const { addDraft, editDraft, editingWorksheetId, openActionMenu, isAddOpen, formError } = uiState;
  const [pendingDelete, setPendingDelete] = useState<WorksheetResponse | null>(null);

  // --- Local mode state ---
  const [localDrafts, setLocalDrafts] = useState<WorksheetDraft[]>([]);
  const localWorksheets = useMemo(() => localDrafts.map(draftToResponse), [localDrafts]);
  const localTotalHours = useMemo(() => localDrafts.reduce((sum, d) => {
    const h = typeof d.hours === 'number' ? d.hours : Number(String(d.hours).replace(',', '.'));
    return sum + (Number.isFinite(h) ? h : 0);
  }, 0), [localDrafts]);
  const localTotalOutlay = useMemo(() => localDrafts.filter(d => d.sleptOnJob).length, [localDrafts]);

  // --- Resolve worksheets source ---
  const worksheets = localMode ? localWorksheets : rest.worksheets;
  const totalHours = localMode ? localTotalHours : rest.totalHours;
  const totalOutlay = localMode ? localTotalOutlay : rest.totalOutlay;
  const isSaving = localMode ? false : rest.isSaving;
  const isDeleting = localMode ? false : rest.isDeleting;

  const isDetailList = variant === 'list';
  const sortedWorksheets = useMemo(
    () => [...worksheets].sort((a, b) => {
      if (isDetailList) {
        const leftName = displayNameFor(a.userId);
        const rightName = displayNameFor(b.userId);
        const byName = leftName.localeCompare(rightName, 'da-DK', { sensitivity: 'base' });
        if (byName !== 0) return byName;
      }
      return b.workDate.localeCompare(a.workDate);
    }),
    [worksheets, isDetailList, displayNameFor],
  );
  const totalHoursValue = parseNullableNumber(totalHours);
  const totalOutlayValue = parseNullableNumber(totalOutlay);
  const openActionWorksheet = openActionMenu
    ? sortedWorksheets.find((worksheet) => worksheet.id === openActionMenu.worksheetId) ?? null
    : null;
  const isScrollableList = variant === 'section';

  // --- Effects ---
  useEffect(() => {
    if (localMode) return;
    if (!editingWorksheetId) return;
    if (worksheets.some((worksheet) => worksheet.id === editingWorksheetId)) return;
    dispatch({ type: 'missingEditingWorksheet' });
  }, [editingWorksheetId, worksheets, localMode]);

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
    const scrollContainer = document.querySelector('.app-shell');
    scrollContainer?.addEventListener('scroll', closeMenu, { passive: true });
    window.addEventListener('resize', closeMenu);
    return () => {
      scrollContainer?.removeEventListener('scroll', closeMenu);
      window.removeEventListener('resize', closeMenu);
    };
  }, [openActionMenu]);

  // --- Local mode: update parent on change ---
  const { onChange } = localMode ? rest : { onChange: undefined };
  useEffect(() => {
    if (!localMode || !onChange) return;
    onChange(localDrafts);
  }, [localDrafts, localMode, onChange]);

  // --- Validation ---
  const validateDraft = (draft: WorksheetDraft, currentWorksheetId?: string): number | null => {
    const result = validateWorksheetDraft(draft, worksheets, currentWorksheetId);
    if ('error' in result) {
      dispatch({ type: 'setFormError', error: result.error });
      return null;
    }
    return result.hours;
  };

  // --- Save ---
  const saveDraft = async (draft: WorksheetDraft, worksheetId?: string) => {
    dispatch({ type: 'setFormError', error: null });
    const hoursWorked = validateDraft(draft, worksheetId);
    if (hoursWorked === null) return;

    if (localMode) {
      const entry: WorksheetDraft = {
        id: worksheetId ?? crypto.randomUUID(),
        workDate: dateKey(draft.workDate),
        userId: draft.userId,
        hours: hoursWorked,
        sleptOnJob: draft.sleptOnJob,
      };
      setLocalDrafts(prev => worksheetId
        ? prev.map(ts => ts.id === worksheetId ? entry : ts)
        : [...prev, entry]
      );
    } else {
      try {
        await rest.onUpsert({
          id: worksheetId,
          jobId: rest.jobId,
          userId: draft.userId,
          userDisplayName: displayNameFor(draft.userId),
          workDate: dateKey(draft.workDate),
          hoursWorked,
          sleptOnJob: draft.sleptOnJob,
        });
      } catch {
        return;
      }
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
        hours: parseHours(worksheet.hoursWorked),
        sleptOnJob: worksheet.sleptOnJob,
      },
    });
  };

  const handleDelete = (worksheet: WorksheetResponse) => {
    if (localMode) {
      setLocalDrafts(prev => prev.filter(ts => ts.id !== worksheet.id));
      dispatch({ type: 'deleteStarted', worksheetId: worksheet.id });
      return;
    }
    dispatch({ type: 'deleteStarted', worksheetId: worksheet.id });
    setPendingDelete(worksheet);
  };

  const confirmDelete = () => {
    if (!pendingDelete) return;
    rest.onDelete({ worksheetId: pendingDelete.id, jobId: rest.jobId });
    setPendingDelete(null);
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
        displayNameFor={displayNameFor}
        userOptions={userOptions}
        canPickUser={canPickUser}
        currentUserName={currentUserName}
        addDraft={addDraft}
        editDraft={editDraft}
        editingWorksheetId={editingWorksheetId}
        openActionMenu={openActionMenu}
        isAddOpen={isAddOpen}
        isLoadingUsers={isLoadingUsers}
        isSaving={isSaving}
        formError={formError}
        totalHoursValue={totalHoursValue}
        totalOutlayValue={totalOutlayValue}
        isScrollableList={isScrollableList}
        isDetailList={isDetailList}
        onToggleActionMenu={toggleActionMenu}
        onEditDraftChange={(draft) => dispatch({ type: 'setEditDraft', draft })}
        onSaveEdit={(draft, worksheetId) => saveDraft(draft, worksheetId)}
        onCancelEdit={() => dispatch({ type: 'cancelEdit' })}
        onOpenAddForm={() => dispatch({ type: 'openAdd', defaultUserId })}
        onAddDraftChange={(draft) => dispatch({ type: 'setAddDraft', draft })}
        onSaveAdd={(draft) => saveDraft(draft)}
        onCancelAdd={() => dispatch({ type: 'cancelAdd', defaultUserId })}
      />

      <WorksheetActionMenuPortal
        openActionMenu={openActionMenu}
        openActionWorksheet={openActionWorksheet}
        isDeleting={isDeleting}
        canDelete={canPickUser || openActionWorksheet?.userId === user?.id}
        onStartEdit={startEdit}
        onDelete={handleDelete}
      />

      {!localMode && (
        <ConfirmDeleteDialog
          open={pendingDelete !== null}
          title="Slet timeseddel"
          message="Er du sikker på, du vil slette denne timeseddel?"
          onConfirm={confirmDelete}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </>
  );
}
