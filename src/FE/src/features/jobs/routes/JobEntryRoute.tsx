import { useEffect } from 'react';
import { Navigate, useLocation, useParams } from 'react-router-dom';
import { useGetApiJobsId } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { ErrorState } from '../../../components/ErrorState';
import { notify } from '../../../lib/toast';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { AdminCompletedJobReport } from './AdminCompletedJobReport';
import { CompletedJobReport } from './CompletedJobReport';
import { JobDetail } from './JobDetail';

type JobEntryLocationState = {
  from?: string;
  readOnly?: boolean;
  forceEdit?: boolean;
};

function shouldOpenReport(
  status: JobStatus,
  jobType: string | null | undefined,
  isAdmin: boolean,
  readOnly: boolean,
  forceRejectedAdminEdit: boolean,
): boolean {
  if (forceRejectedAdminEdit) return false;
  if (readOnly || jobType === 'Diverse') return true;
  if (status === JobStatus.InReview || status === JobStatus.Approved) return true;
  return isAdmin && status === JobStatus.Rejected;
}

export function JobEntryRoute() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const isAdmin = useIsAdmin();
  const state = (location.state as JobEntryLocationState | null) ?? undefined;
  const query = useGetApiJobsId(id ?? '', {
    query: { enabled: Boolean(id) },
  });

  const status = query.data?.status;
  const reopenReason = query.data?.rejectionNote?.trim();

  useEffect(() => {
    if (status !== JobStatus.Reopened) return;

    notify.warning(
      reopenReason
        ? `Sagen er genåbnet: ${reopenReason}`
        : 'Sagen er genåbnet og kan redigeres igen.',
    );
  }, [status, reopenReason]);

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

  const readOnly = Boolean(state?.readOnly);
  const forceRejectedAdminEdit = Boolean(state?.forceEdit)
    && isAdmin
    && !readOnly
    && query.data.status === JobStatus.Rejected;
  const reportMode = shouldOpenReport(
    query.data.status,
    query.data.jobType,
    isAdmin,
    readOnly,
    forceRejectedAdminEdit,
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

  const useAdminReferenceView = isAdmin
    && !readOnly
    && query.data.status === JobStatus.Rejected;

  return useAdminReferenceView ? <AdminCompletedJobReport /> : <CompletedJobReport />;
}
