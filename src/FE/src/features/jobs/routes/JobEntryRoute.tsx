import { useEffect } from 'react';
import { Navigate, useLocation, useParams } from 'react-router-dom';
import { useGetApiJobsId } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { ErrorState } from '../../../components/ErrorState';
import { notify } from '../../../lib/toast';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { AdminCompletedJobReport } from './AdminCompletedJobReport';
import { JobDetail } from './JobDetail';

type JobEntryLocationState = {
  from?: string;
  readOnly?: boolean;
  forceEdit?: boolean;
};

// States a viewer can still take through the editing wizard. Draft is the initial
// authoring state; Rejected and Reopened are handed back for correction (the
// assignee fixes the case and resubmits — see the rejection-correction lifecycle).
// InReview and Approved are locked: they only ever open the read/overview report.
const EDITABLE_STATES = new Set<JobStatus>([
  JobStatus.Draft,
  JobStatus.Rejected,
  JobStatus.Reopened,
]);

// The completed-job overview (WOR-701) is the read surface for every state and
// viewer, reached through the /app/completed view route. The wizard stays reachable
// through the /app/job edit route for states that can still be edited, so a rejected
// case can be corrected and resubmitted. Routing therefore follows the URL's intent
// and only redirects when a state is incompatible with the requested surface.
function shouldOpenReport(
  status: JobStatus,
  jobType: string | null | undefined,
  readOnly: boolean,
  allowForceEdit: boolean,
  isEditRoute: boolean,
): boolean {
  // An admin can drop an editable (non-approved) case into the wizard from the overview.
  if (allowForceEdit) return false;
  if (readOnly || jobType === 'Diverse') return true;
  if (isEditRoute) {
    // Edit intent: keep editable states in the wizard; locked states fall back to the report.
    return !EDITABLE_STATES.has(status);
  }
  // View intent: everything reads as the report except a Draft, which has no report yet.
  return status !== JobStatus.Draft;
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
  const allowForceEdit = Boolean(state?.forceEdit)
    && isAdmin
    && !readOnly
    && query.data.status !== JobStatus.Approved;
  const currentMode = location.pathname.includes('/completed/') ? 'report' : 'edit';
  const reportMode = shouldOpenReport(
    query.data.status,
    query.data.jobType,
    readOnly,
    allowForceEdit,
    currentMode === 'edit',
  );
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

  // The completed-job overview is the single read/decision surface for every report-mode
  // state and every viewer. Editing continues through the wizard (JobDetail).
  return reportMode ? <AdminCompletedJobReport /> : <JobDetail />;
}
