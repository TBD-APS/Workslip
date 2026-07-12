import { useEffect, useMemo, useReducer, type MouseEvent } from 'react';
import { useAuth } from '../../../../providers/useAuth';
import { useCan } from '../../../../providers/permissions';
import type { UserViewModel, WorksheetResponse } from '../../../../api/generated/models';
import { useGetApiUsers } from '../../../../api/generated/users/users';
import { parseNullableNumber } from '../../../../lib/formatUtils';
import { WorksheetsSection } from '../../components/WorksheetsSection';
import { WorksheetActionMenuPortal } from '../../components/WorksheetActionMenuPortal';
import { initialWorksheetUiState, worksheetUiReducer, dateKey, parseHours, validateWorksheetDraft } from '../../components/worksheetUtils';
import type { WorksheetDraft } from '../../components/worksheetUtils';

type JobWorksheetsStepProps = {
  jobId: string;
  worksheets: WorksheetResponse[];
  totalHours: number | string | null;
  totalOutlay: number | string | null;
  assignableUsers: UserViewModel[];
  isLoadingUsers: boolean;
  isSaving: boolean;
  isDeleting: boolean;
  onUpsert: (params: { id?: string; jobId: string; userId: string; userDisplayName: string; workDate: string; hoursWorked: number; sleptOnJob: boolean }) => Promise<unknown>;
  onDelete: (params: { worksheetId: string; jobId: string }) => void;
  variant?: 'section' | 'list';
};

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
  variant = 'section',
}: JobWorksheetsStepProps) {
  const { user } = useAuth();
  const canPickUser = useCan('worksheet:assign');
  const usersQuery = useGetApiUsers({ limit: 20 }, { query: { enabled: canPickUser } });
  const resolvedUsers = useMemo(
    () => (canPickUser
      ? (assignableUsers.length > 0 ? assignableUsers : null)
      : []) ?? [],
    [canPickUser, assignableUsers, usersQuery.data],
  );
  const defaultUserId = canPickUser
    ? (user?.email ? (resolvedUsers.find((u) => u.email === user.email)?.id ?? '') : '')
    : (user?.id ?? '');
  const userOptions = resolvedUsers.map((u) => ({ id: u.id, label: u.displayName, description: u.email }));
  const currentUserName = user?.displayName ?? user?.email ?? 'dig';

  // Non-admins only see their own worksheets; show the current user's name on
  // every row regardless of who actually created the entry.
  const displayNameFor = (userId: string): string => {
    if (!canPickUser) return currentUserName;
    return resolvedUsers.find((u) => u.id === userId)?.displayName ?? userId.slice(0, 8);
  };

  const [uiState, dispatch] = useReducer(worksheetUiReducer, defaultUserId, initialWorksheetUiState);
  const { addDraft, editDraft, editingWorksheetId, openActionMenu, isAddOpen, formError } = uiState;

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
    const scrollContainer = document.querySelector('.app-shell');
    scrollContainer?.addEventListener('scroll', closeMenu, { passive: true });
    window.addEventListener('resize', closeMenu);

    return () => {
      scrollContainer?.removeEventListener('scroll', closeMenu);
      window.removeEventListener('resize', closeMenu);
    };
  }, [openActionMenu]);

  const validateDraft = (draft: WorksheetDraft, currentWorksheetId?: string): number | null => {
    const result = validateWorksheetDraft(draft, worksheets, currentWorksheetId);
    if ('error' in result) {
      dispatch({ type: 'setFormError', error: result.error });
      return null;
    }
    return result.hours;
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
        userDisplayName: displayNameFor(draft.userId),
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
        hours: parseHours(worksheet.hoursWorked),
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
    </>
  );
}
