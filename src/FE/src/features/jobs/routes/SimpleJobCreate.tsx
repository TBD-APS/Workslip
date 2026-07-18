import { useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useLocation } from 'react-router-dom';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { getGetApiJobsQueryKey, usePostApiJobsIdStatus } from '../../../api/generated/jobs/jobs';
import { useJobCreate } from '../hooks/useJobCreate';
import { NavigationGuard } from '../../../components/forms/NavigationGuard';
import { emptyForm, getLinkableJobs, sameForm } from '../utils';
import type { JobForm } from '../types';
import type { CustomerSnapshotData } from '../../../api/generated/models/customerSnapshotData';
import type { JobListItemViewModel, WorksheetResponse } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';
import { useAuth } from '../../../providers/useAuth';
import { WorksheetsSection } from '../components/WorksheetsSection';
import { WorksheetActionMenuPortal } from '../components/WorksheetActionMenuPortal';
import { CreateOverviewStep } from '../components/steps/CreateOverviewStep';
import {
  type WorksheetDraft,
  type UserOption,
  initialWorksheetUiState,
  worksheetUiReducer,
  parseHours,
  dateKey,
  validateWorksheetDraft,
} from '../components/worksheetUtils';

type JobCreateLocationState = {
  fromCustomer?: boolean;
  customerId?: string;
  customerSnapshot?: CustomerSnapshotData;
};

function draftToResponse(draft: WorksheetDraft): WorksheetResponse {
  return {
    id: draft.id ?? '',
    userId: draft.userId,
    workDate: draft.workDate,
    hoursWorked: String(draft.hours),
    sleptOnJob: draft.sleptOnJob,
  } as WorksheetResponse;
}

const SimpleJobCreate = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const locationState = location.state as JobCreateLocationState | null;
  const { user } = useAuth();

  const initialForm: JobForm = locationState?.fromCustomer && locationState.customerSnapshot
    ? {
        ...emptyForm,
        customerId: locationState.customerId ?? null,
        customerSnapshot: { ...locationState.customerSnapshot },
        jobType: 'Diverse',
      }
    : { ...emptyForm, jobType: 'Diverse' };
  const initialFormRef = useRef(initialForm);

  const [createdJobId, setCreatedJobId] = useState<string | null>(null);
  const [localTimesheets, setLocalTimesheets] = useState<WorksheetDraft[]>([]);
  const [pendingSave, setPendingSave] = useState(false);
  const defaultUserId = user?.id ?? '';
  const currentUserName = user?.displayName ?? user?.email ?? 'dig';

  const [uiState, uiDispatch] = useReducer(
    worksheetUiReducer,
    defaultUserId,
    (id: string) => initialWorksheetUiState(id, true),
  );

  const { data: jobsData, isLoading: isLoadingJobs } = useQuery({
    queryKey: getGetApiJobsQueryKey({ status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 }),
    queryFn: async () => {
      const data = await apiClient.get('/api/jobs', { params: { status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 } }) as { items: JobListItemViewModel[]; totalCount: number };
      return data.items;
    },
  });
  const linkableJobs = getLinkableJobs(jobsData, undefined);

  const statusMutation = usePostApiJobsIdStatus();
  const create = useJobCreate((jobId) => {
    statusMutation.mutate(
      { id: jobId, data: { status: JobStatus.InReview } },
      {
        onSuccess: () => setCreatedJobId(jobId),
        onError: () => {
          // surface an error to the user instead of showing a false "success" dialog
          uiDispatch({ type: 'setFormError', error: 'Jobbet blev oprettet, men kunne ikke sendes til gennemgang.' });
        },
      },
    );
  }, initialForm);

  const userOptions: UserOption[] = useMemo(
    () => create.assignableUsers.map(u => ({ id: u.id, label: u.displayName, description: u.email })),
    [create.assignableUsers],
  );

  const displayNameFor = useMemo(() => {
    return (userId: string): string => {
      const found = create.assignableUsers.find(u => u.id === userId);
      return found?.displayName ?? currentUserName;
    };
  }, [create.assignableUsers, currentUserName]);

  useEffect(() => {
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  useEffect(() => {
    if (pendingSave) {
      setPendingSave(false);
      create.save();
    }
  }, [pendingSave]);

  useEffect(() => {
    if (uiState.editingWorksheetId) {
      requestAnimationFrame(() => {
        const el = document.querySelector('.worksheet-list-item--editing .worksheet-form');
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      });
    }
  }, [uiState.editingWorksheetId]);

  useEffect(() => {
    if (!uiState.openActionMenu) return;
    const handlePointerDown = (event: PointerEvent) => {
      if (event.target instanceof Element && event.target.closest('.worksheet-actions-menu-root, .worksheet-actions-menu')) return;
      uiDispatch({ type: 'closeActionMenu' });
    };
    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [uiState.openActionMenu]);

  useEffect(() => {
    if (!uiState.openActionMenu) return;
    const closeMenu = () => uiDispatch({ type: 'closeActionMenu' });
    const scrollContainer = document.querySelector('.app-shell');
    scrollContainer?.addEventListener('scroll', closeMenu, { passive: true });
    window.addEventListener('resize', closeMenu);
    return () => {
      scrollContainer?.removeEventListener('scroll', closeMenu);
      window.removeEventListener('resize', closeMenu);
    };
  }, [uiState.openActionMenu]);

  const validateDraft = (draft: WorksheetDraft): number | null => {
    const result = validateWorksheetDraft(draft, localTimesheets);
    if ('error' in result) {
      uiDispatch({ type: 'setFormError', error: result.error });
      return null;
    }
    return result.hours;
  };

  const handleCreateAnother = () => {
    const preservedCustomerId = create.form.customerId;
    const preservedSnapshot = create.form.customerSnapshot;
    create.reset({ customerId: preservedCustomerId, customerSnapshot: preservedSnapshot });
    initialFormRef.current = { ...emptyForm, jobType: 'Diverse', customerId: preservedCustomerId, customerSnapshot: preservedSnapshot };
    setCreatedJobId(null);
    setLocalTimesheets([]);
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const hasValidHours = localTimesheets.some(ts => {
    const h = typeof ts.hours === 'number' ? ts.hours : Number(String(ts.hours).replace(',', '.'));
    return Number.isFinite(h) && h > 0;
  });
  const canCreateJob = hasValidHours;

  const totalHours = localTimesheets.reduce((sum, ts) => {
    const h = typeof ts.hours === 'number' ? ts.hours : Number(String(ts.hours).replace(',', '.'));
    return sum + (Number.isFinite(h) ? h : 0);
  }, 0);
  const totalOutlay = localTimesheets.filter(ts => ts.sleptOnJob).length;

  const handleSaveAdd = (draft: WorksheetDraft) => {
    uiDispatch({ type: 'setFormError', error: null });
    const hoursNumber = validateDraft(draft);
    if (hoursNumber === null) return;

    setLocalTimesheets(prev => [...prev, {
      id: crypto.randomUUID(),
      workDate: dateKey(draft.workDate),
      userId: draft.userId,
      hours: hoursNumber,
      sleptOnJob: draft.sleptOnJob,
    }]);
    uiDispatch({ type: 'saveSucceeded', defaultUserId });
  };

  const handleSaveEdit = (draft: WorksheetDraft, worksheetId: string) => {
    uiDispatch({ type: 'setFormError', error: null });
    const hoursNumber = validateDraft(draft);
    if (hoursNumber === null) return;

    setLocalTimesheets(prev => prev.map(ts =>
      ts.id === worksheetId
        ? { ...ts, workDate: dateKey(draft.workDate), userId: draft.userId, hours: hoursNumber, sleptOnJob: draft.sleptOnJob }
        : ts
    ));
    uiDispatch({ type: 'saveSucceeded', worksheetId, defaultUserId });
  };

  const handleDeleteTimesheet = (worksheetId: string) => {
    uiDispatch({ type: 'deleteStarted', worksheetId });
    setLocalTimesheets(prev => prev.filter(ts => ts.id !== worksheetId));
  };

  const handleSave = () => {
    if (!canCreateJob) return;
    create.updateTimesheets(localTimesheets);
    setPendingSave(true);
  };

  const hasUnsavedChanges = createdJobId === null && (!sameForm(create.form, initialFormRef.current) || create.linkedJobIds.length > 0 || localTimesheets.length > 0);

  const sortedWorksheets = useMemo(
    () => [...localTimesheets].sort((a, b) => a.workDate.localeCompare(b.workDate)),
    [localTimesheets],
  );

  const sortedAsResponse = useMemo(
    () => sortedWorksheets.map(draftToResponse),
    [sortedWorksheets],
  );

  const openActionWorksheet = uiState.openActionMenu
    ? sortedAsResponse.find(ws => ws.id === uiState.openActionMenu!.worksheetId) ?? null
    : null;

  return (
    <div className="page-container">
      <NavigationGuard when={hasUnsavedChanges} />
      <div className="detail-header">
        <button className="btn-icon" onClick={() => navigate(-1)} aria-label="Tilbage">
          <ArrowLeft size={22} />
        </button>
        <div>
          <h2 className="detail-title">Simpelt job</h2>
        </div>
      </div>

      <CreateOverviewStep
        create={create}
        linkableJobs={linkableJobs}
        isLoadingJobs={isLoadingJobs}
      />

      <WorksheetsSection
        sortedWorksheets={sortedAsResponse}
        displayNameFor={displayNameFor}
        userOptions={userOptions}
        canPickUser={create.assignableUsers.length > 0}
        currentUserName={currentUserName}
        addDraft={uiState.addDraft}
        editDraft={uiState.editDraft}
        editingWorksheetId={uiState.editingWorksheetId}
        openActionMenu={uiState.openActionMenu}
        isAddOpen={uiState.isAddOpen}
        isLoadingUsers={create.isLoadingUsers}
        isSaving={create.isSaving}
        formError={uiState.formError}
        totalHoursValue={totalHours}
        totalOutlayValue={totalOutlay}
        isScrollableList={false}
        isDetailList={false}
        onToggleActionMenu={(event, worksheetId) => {
          const rect = event.currentTarget.getBoundingClientRect();
          uiDispatch({
            type: 'toggleActionMenu',
            worksheetId,
            top: rect.bottom + 6,
            right: window.innerWidth - rect.right,
          });
        }}
        onEditDraftChange={(draft) => uiDispatch({ type: 'setEditDraft', draft })}
        onSaveEdit={(draft, worksheetId) => handleSaveEdit(draft, worksheetId)}
        onCancelEdit={() => uiDispatch({ type: 'cancelEdit' })}
        onOpenAddForm={() => uiDispatch({ type: 'openAdd', defaultUserId })}
        onAddDraftChange={(draft) => uiDispatch({ type: 'setAddDraft', draft })}
        onSaveAdd={(draft) => handleSaveAdd(draft)}
        onCancelAdd={() => uiDispatch({ type: 'cancelAdd', defaultUserId })}
      />

      <WorksheetActionMenuPortal
        openActionMenu={uiState.openActionMenu}
        openActionWorksheet={openActionWorksheet}
        isDeleting={false}
        canDelete={true}
        onStartEdit={(worksheet) => {
          uiDispatch({
            type: 'toggleEdit',
            worksheetId: worksheet.id,
            draft: {
              userId: worksheet.userId,
              workDate: dateKey(worksheet.workDate),
              hours: parseHours(worksheet.hoursWorked),
              sleptOnJob: worksheet.sleptOnJob,
            },
          });
        }}
        onDelete={(worksheet) => handleDeleteTimesheet(worksheet.id)}
      />

      <div className="step-nav">
        <button className="step-nav-btn step-nav-btn-back" onClick={() => navigate(-1)}>
          Tilbage
        </button>
        <button
          className="step-nav-btn step-nav-btn-next step-nav-btn-next--wide"
          onClick={handleSave}
          disabled={create.isSaving || !canCreateJob}
        >
          {create.isSaving ? <Loader2 className="animate-spin" size={18} /> : null}
          <span>{create.isSaving ? 'Gemmer...' : 'Opret job'}</span>
        </button>
      </div>

      {createdJobId && (
        <CreateSuccessDialog
          onCreateAnother={handleCreateAnother}
          onGoToJobList={() => navigate('/app')}
        />
      )}
    </div>
  );
};

function CreateSuccessDialog({
  onCreateAnother,
  onGoToJobList,
}: {
  onCreateAnother: () => void;
  onGoToJobList: () => void;
}) {
  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="create-success-title">
      <div className="modal-card">
        <h3 id="create-success-title">Jobbet er oprettet</h3>
        <div className="modal-actions">
          <button className="btn btn-secondary" onClick={onCreateAnother}>
            Opret et mere
          </button>
          <button className="btn btn-primary" onClick={onGoToJobList}>
            Til joblisten
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

export { SimpleJobCreate };
