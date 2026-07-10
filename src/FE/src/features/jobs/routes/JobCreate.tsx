import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useLocation } from 'react-router-dom';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';
import { useJobCreate } from '../hooks/useJobCreate';
import { CreateOverviewStep } from '../components/steps/CreateOverviewStep';
import { NavigationGuard } from '../../../components/forms/NavigationGuard';
import { emptyForm, getLinkableJobs, sameForm } from '../utils';
import type { JobForm } from '../types';
import type { CustomerSnapshotData } from '../../../api/generated/models/customerSnapshotData';
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';

type JobCreateLocationState = {
  fromCustomer?: boolean;
  customerId?: string;
  customerSnapshot?: CustomerSnapshotData;
};

export const JobCreate = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const locationState = location.state as JobCreateLocationState | null;

  const initialForm: JobForm | undefined = locationState?.fromCustomer && locationState.customerSnapshot
    ? {
        ...emptyForm,
        customerId: locationState.customerId ?? null,
        customerSnapshot: { ...locationState.customerSnapshot },
      }
    : undefined;
  const initialFormRef = useRef(initialForm ?? emptyForm);

  const [createdJobId, setCreatedJobId] = useState<string | null>(null);
  const { data: jobsData, isLoading: isLoadingJobs } = useQuery({
    queryKey: getGetApiJobsQueryKey({ status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 }),
    queryFn: async () => {
      const data = await apiClient.get('/api/jobs', { params: { status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 } }) as { items: JobListItemViewModel[]; totalCount: number };
      return data.items;
    },
  });
  const linkableJobs = getLinkableJobs(jobsData, undefined);

  const create = useJobCreate((jobId) => setCreatedJobId(jobId), initialForm);

  useEffect(() => {
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  const handleCreateAnother = () => {
    create.reset();
    setCreatedJobId(null);
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const hasUnsavedChanges = createdJobId === null && (!sameForm(create.form, initialFormRef.current) || create.linkedJobIds.length > 0);

  return (
    <div className="page-container">
      <NavigationGuard when={hasUnsavedChanges} />
      <div className="detail-header">
        <button className="btn-icon" onClick={() => navigate('/app')} aria-label="Tilbage">
          <ArrowLeft size={22} />
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

      <div className="step-nav">
        <button className="step-nav-btn step-nav-btn-back" onClick={() => navigate('/app')}>
          Tilbage
        </button>
        <button
          className="step-nav-btn step-nav-btn-next step-nav-btn-next--wide"
          onClick={create.save}
          disabled={create.isSaving}
        >
          {create.isSaving ? <Loader2 className="animate-spin" size={18} /> : null}
          <span>{create.isSaving ? 'Gemmer...' : 'Opret sag'}</span>
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
        <h3 id="create-success-title">Sagen er oprettet</h3>
        <div className="modal-actions">
          <button className="btn btn-secondary" onClick={onCreateAnother}>
            Opret en mere
          </button>
          <button className="btn btn-primary" onClick={onGoToJobList}>
            Til sagslisten
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
