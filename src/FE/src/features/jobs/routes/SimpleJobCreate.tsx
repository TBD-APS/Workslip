import { useEffect, useRef, useState } from 'react';
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
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';
import { JobWorksheetsStep } from '../components/steps/JobWorksheetsStep';
import { CreateOverviewStep } from '../components/steps/CreateOverviewStep';
import type { WorksheetDraft } from '../components/worksheetUtils';

type JobCreateLocationState = {
  fromCustomer?: boolean;
  customerId?: string;
  customerSnapshot?: CustomerSnapshotData;
};

const SimpleJobCreate = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const locationState = location.state as JobCreateLocationState | null;

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
  const [localWorksheets, setLocalWorksheets] = useState<WorksheetDraft[]>([]);
  const [pendingSave, setPendingSave] = useState(false);

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
        onError: () => {},
      },
    );
  }, initialForm);

  useEffect(() => {
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  useEffect(() => {
    if (pendingSave) {
      setPendingSave(false);
      create.save();
    }
  }, [pendingSave]);

  const handleCreateAnother = () => {
    const preservedCustomerId = create.form.customerId;
    const preservedSnapshot = create.form.customerSnapshot;
    create.reset({ customerId: preservedCustomerId, customerSnapshot: preservedSnapshot });
    initialFormRef.current = { ...emptyForm, jobType: 'Diverse', customerId: preservedCustomerId, customerSnapshot: preservedSnapshot };
    setCreatedJobId(null);
    setLocalWorksheets([]);
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleGoToCreatedJob = () => {
    if (!createdJobId) return;
    navigate(`/app/job/${createdJobId}`, { replace: true, state: { from: '/app' } });
  };

  const hasValidHours = localWorksheets.some(ts => {
    const h = typeof ts.hours === 'number' ? ts.hours : Number(String(ts.hours).replace(',', '.'));
    return Number.isFinite(h) && h > 0;
  });
  const canCreateJob = hasValidHours;

  const handleSave = () => {
    if (!canCreateJob) return;
    create.updateTimesheets(localWorksheets);
    setPendingSave(true);
  };

  const hasUnsavedChanges = createdJobId === null && (!sameForm(create.form, initialFormRef.current) || create.linkedJobIds.length > 0 || localWorksheets.length > 0);

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

      <JobWorksheetsStep
        localMode
        assignableUsers={create.assignableUsers}
        isLoadingUsers={create.isLoadingUsers}
        variant="list"
        onChange={setLocalWorksheets}
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
          onGoToJob={handleGoToCreatedJob}
        />
      )}
    </div>
  );
};

function CreateSuccessDialog({
  onCreateAnother,
  onGoToJobList,
  onGoToJob,
}: {
  onCreateAnother: () => void;
  onGoToJobList: () => void;
  onGoToJob: () => void;
}) {
  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="create-success-title">
      <div className="modal-card">
        <h3 id="create-success-title">Jobbet er oprettet</h3>
        <div className="modal-actions modal-actions--triple">
          <button className="btn btn-secondary" onClick={onCreateAnother}>
            Opret et mere
          </button>
          <button className="btn btn-secondary" onClick={onGoToJobList}>
            Til joblisten
          </button>
          <button className="btn btn-primary" onClick={onGoToJob}>
            Til sagen
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

export { SimpleJobCreate };
