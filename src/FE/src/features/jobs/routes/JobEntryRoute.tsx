import { Navigate, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useGetApiJobsId } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { ErrorState } from '../../../components/ErrorState';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { CompletedJobReport } from './CompletedJobReport';
import { JobDetail } from './JobDetail';

type JobEntryLocationState = {
  from?: string;
  readOnly?: boolean;
};

function shouldOpenReport(
  status: JobStatus,
  jobType: string | null | undefined,
  isAdmin: boolean,
  readOnly: boolean,
): boolean {
  if (readOnly || jobType === 'Diverse') return true;
  if (status === JobStatus.InReview || status === JobStatus.Approved) return true;
  return isAdmin && status === JobStatus.Rejected;
}

export function JobEntryRoute() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const state = (location.state as JobEntryLocationState | null) ?? undefined;
  const query = useGetApiJobsId(id ?? '', {
    query: { enabled: Boolean(id) },
  });

  if (!id) {
    return <Navigate to="/app" replace />;
  }

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="detail-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
      </div>
    );
  }

  if (query.isError || !query.data) {
    return (
      <div className="page-container">
        <ErrorState message="Kunne ikke hente sagen." onRetry={() => query.refetch()} />
      </div>
    );
  }

  const reportMode = shouldOpenReport(
    query.data.status,
    query.data.jobType,
    isAdmin,
    Boolean(state?.readOnly),
  );
  const currentMode = location.pathname.includes('/completed/') ? 'report' : 'edit';
  const targetMode = reportMode ? 'report' : 'edit';

  if (currentMode !== targetMode) {
    return (
      <Navigate
        to={targetMode === 'report' ? `/app/completed/${id}` : `/app/job/${id}`}
        replace
        state={state}
      />
    );
  }

  if (!reportMode) {
    return <JobDetail />;
  }

  return (
    <>
      <CompletedJobReport />
      {!state?.readOnly && (
        <div className="page-container">
          <div className="edit-form-bottom-actions">
            <button
              className="btn btn-secondary edit-form-bottom-btn"
              type="button"
              onClick={() => navigate('/app', { replace: true })}
            >
              Tilbage til opgaver
            </button>
          </div>
        </div>
      )}
    </>
  );
}
