import { useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { useGetApiJobs } from '../../../api/generated/jobs/jobs';
import { useJobCreate } from '../hooks/useJobCreate';
import { CreateOverviewStep } from '../components/steps/CreateOverviewStep';
import { getLinkableJobs } from '../utils';
import { JobStatus } from '../../../api/generated/models';

export const JobCreate = () => {
  const navigate = useNavigate();
  const [createdJobId, setCreatedJobId] = useState<string | null>(null);
  const { data: jobsData, isLoading: isLoadingJobs } = useGetApiJobs({ 
    status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview],
    limit: 200 
  });
  const linkableJobs = getLinkableJobs(jobsData, undefined);

  const create = useJobCreate((jobId) => setCreatedJobId(jobId));

  const handleCreateAnother = () => {
    create.reset();
    setCreatedJobId(null);
    document.querySelector('.app-content')?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  return (
    <div className="page-container">
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
          disabled={create.isSaving || !create.canSave}
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
