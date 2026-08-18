import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useLocation } from 'react-router-dom';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';
import { useJobCreateWithAuditorScope } from '../hooks/useJobCreateWithAuditorScope';
import { CreateOverviewStep } from '../components/steps/CreateOverviewStep';
import { JobAuditorScopeControl } from '../components/JobAuditorScopeControl';
import { NavigationGuard } from '../../../components/forms/NavigationGuard';
import { useModalAccessibility } from '../../../components/common/useModalAccessibility';
import { emptyForm, getLinkableJobs, sameForm } from '../utils';
import type { JobForm } from '../types';
import type { CustomerSnapshotData } from '../../../api/generated/models/customerSnapshotData';
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';
import { useIsAdmin } from '../../../providers/permissions';

type JobCreateLocationState = {
  fromCustomer?: boolean;
  customerId?: string;
  customerSnapshot?: CustomerSnapshotData;
};

export const JobCreate = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const isAdmin = useIsAdmin();
  const locationState = location.state as JobCreateLocationState | null;

  const initialForm: JobForm | undefined = locationState?.fromCustomer && locationState.customerSnapshot
    ? {
        ...emptyForm,
        customerId: locationState.customerId ?? null,
        customerSnapshot: { ...locationState.customerSnapshot },
      }
    : undefined;
  const [initialFormBaseline, setInitialFormBaseline] = useState<JobForm>(() => initialForm ?? emptyForm);

  const [createdJobIds, setCreatedJobIds] = useState<string[]>([]);
  const { data: jobsData, isLoading: isLoadingJobs } = useQuery({
    queryKey: getGetApiJobsQueryKey({ status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 }),
    queryFn: async () => {
      const data = await apiClient.get('/api/jobs', { params: { status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 } }) as { items: JobListItemViewModel[]; totalCount: number };
      return data.items;
    },
  });
  const linkableJobs = getLinkableJobs(jobsData, undefined);

  const create = useJobCreateWithAuditorScope(setCreatedJobIds, initialForm);

  useEffect(() => {
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  const handleCreateAnother = () => {
    const preservedCustomerId = create.form.customerId;
    const preservedSnapshot = create.form.customerSnapshot;
    create.reset({
      customerId: preservedCustomerId,
      customerSnapshot: preservedSnapshot,
    });
    setInitialFormBaseline({
      ...emptyForm,
      customerId: preservedCustomerId,
      customerSnapshot: preservedSnapshot,
    });
    setCreatedJobIds([]);
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleGoToCreatedJob = () => {
    if (createdJobIds.length === 0) return;
    navigate(`/app/job/${createdJobIds[0]}`, { replace: true, state: { from: '/app' } });
  };

  const auditorScopeChanged = isAdmin
    && (!create.auditorScope.isInAuditorScope || create.auditorScope.reason.length > 0);
  const hasUnsavedChanges = createdJobIds.length === 0 && (
    !sameForm(create.form, initialFormBaseline)
    || create.linkedJobIds.length > 0
    || auditorScopeChanged
    || create.hasPendingAuditorScope
  );

  return (
    <div className="page-container">
      <NavigationGuard when={hasUnsavedChanges} />
      <div className="detail-header">
        <button className="btn-icon" type="button" onClick={() => navigate(-1)} aria-label="Tilbage">
          <ArrowLeft size={22} aria-hidden="true" />
        </button>
        <div>
          <h2 className="detail-title">Ny sag</h2>
        </div>
      </div>

      <CreateOverviewStep
        create={create}
        linkableJobs={linkableJobs}
        isLoadingJobs={isLoadingJobs}
      />

      {isAdmin && (
        <div className="job-create-auditor-scope">
          <JobAuditorScopeControl
            value={create.auditorScope}
            onChange={create.updateAuditorScope}
            disabled={create.isSaving || create.hasPendingAuditorScope}
          />
          {create.auditorScopeError && (
            <div className="auditor-scope-create-error" role="alert">
              <div>
                <strong>Auditøradgangen blev ikke gemt</strong>
                <p>Sagen er allerede oprettet. Prøv igen her, så du ikke kommer til at oprette en kopi.</p>
              </div>
              <button
                className="btn btn-secondary"
                type="button"
                onClick={create.retryAuditorScope}
                disabled={create.isSaving}
              >
                {create.isSaving ? 'Prøver igen...' : 'Prøv igen'}
              </button>
            </div>
          )}
        </div>
      )}

      <div className="step-nav">
        <button className="step-nav-btn step-nav-btn-back" type="button" onClick={() => navigate(-1)}>
          Tilbage
        </button>
        <button
          className="step-nav-btn step-nav-btn-next step-nav-btn-next--wide"
          type="button"
          onClick={create.save}
          disabled={create.isSaving || create.hasPendingAuditorScope}
        >
          {create.isSaving ? <Loader2 className="animate-spin" size={18} aria-hidden="true" /> : null}
          <span>{create.isSaving ? 'Gemmer...' : 'Opret sag'}</span>
        </button>
      </div>

      {createdJobIds.length > 0 && (
        <CreateSuccessDialog
          createdJobCount={createdJobIds.length}
          onCreateAnother={handleCreateAnother}
          onGoToJobList={() => navigate('/app')}
          onGoToJob={handleGoToCreatedJob}
        />
      )}
    </div>
  );
};

function CreateSuccessDialog({
  createdJobCount,
  onCreateAnother,
  onGoToJobList,
  onGoToJob,
}: {
  createdJobCount: number;
  onCreateAnother: () => void;
  onGoToJobList: () => void;
  onGoToJob: () => void;
}) {
  const primaryButtonRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open: true,
    onClose: onGoToJobList,
    initialFocusRef: primaryButtonRef,
  });

  return createPortal(
    <div className="modal-backdrop">
      <div
        ref={dialogRef}
        className="modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="create-success-title"
        tabIndex={-1}
      >
        <h3 id="create-success-title">
          {createdJobCount > 1 ? `${createdJobCount} sager er oprettet` : 'Sagen er oprettet'}
        </h3>
        <div className="modal-actions modal-actions--triple">
          <button type="button" className="btn btn-secondary" onClick={onCreateAnother}>
            Opret en mere
          </button>
          <button type="button" className="btn btn-secondary" onClick={onGoToJobList}>
            Til sagslisten
          </button>
          <button ref={primaryButtonRef} type="button" className="btn btn-primary" onClick={onGoToJob}>
            {createdJobCount > 1 ? 'Til første sag' : 'Til sagen'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
