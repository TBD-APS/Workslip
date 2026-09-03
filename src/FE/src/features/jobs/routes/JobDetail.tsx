import { useEffect, useRef } from 'react';
import { Navigate, useLocation, useParams, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { JobDetailsPage } from '../components/JobDetails';
import { useJobDetails } from '../hooks/useJobDetails';
import { markJobAsSeen } from '../utils/markJobSeen';
import { useScrollRestore } from '../../../hooks/useScrollRestore';
import { JobStatus } from '../../../api/generated/models';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';

export const JobDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const isAdmin = useIsAdmin();
  const from = (location.state as { from?: string } | undefined)?.from ?? '/app';
  const details = useJobDetails(id);
  const jobStatus = details.job?.status;
  const loadedJobId = details.job?.id;
  const referenceData = details.referenceData;
  const { setCurrentStep } = details;
  const normalizedCorrectionJobIdRef = useRef<string | null>(null);

  useScrollRestore(`job:${id}`);

  useEffect(() => {
    if (!id || !jobStatus) return;
    if (isAdmin && jobStatus === JobStatus.Rejected) return;

    markJobAsSeen(id, queryClient);
  }, [id, jobStatus, isAdmin, queryClient]);

  useEffect(() => {
    if (!id || !loadedJobId || loadedJobId !== id) {
      normalizedCorrectionJobIdRef.current = null;
      return;
    }

    const shouldNormalizeRejected = jobStatus === JobStatus.Rejected && !isAdmin;
    // Reopened jobs have already completed the normal worksheet path. Wait for
    // reference data so this normalization is registered in the same render as
    // (and after) the generic worksheet auto-navigation decision.
    const shouldNormalizeReopened = jobStatus === JobStatus.Reopened && Boolean(referenceData);

    if (!shouldNormalizeRejected && !shouldNormalizeReopened) {
      if (jobStatus !== JobStatus.Reopened) {
        normalizedCorrectionJobIdRef.current = null;
      }
      return;
    }

    if (normalizedCorrectionJobIdRef.current === loadedJobId) return;
    normalizedCorrectionJobIdRef.current = loadedJobId;

    setCurrentStep((step) => (step === 0 ? step : 0));
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  }, [id, loadedJobId, jobStatus, referenceData, setCurrentStep, isAdmin]);

  if (id && details.job?.jobType === 'Diverse') {
    return <Navigate to={`/app/completed/${id}${location.search}`} replace state={{ from }} />;
  }

  if (id && isAdmin && jobStatus === JobStatus.Rejected) {
    return <Navigate to={`/app/completed/${id}${location.search}`} replace state={{ from }} />;
  }

  return (
    <JobDetailsPage
      details={details}
      onBack={() => {
        // 'default' is React Router's key for the entry the app booted on, i.e.
        // exactly the cold push-notification deep link into a single sag. There is
        // nothing behind it to pop, so navigate(-1) dead-ends there and the user is
        // trapped in the wizard. Every warm entry keeps navigate(-1): five call
        // sites open /app/job/:id without a state.from, and sending those users to
        // `from` would dump them on the job list instead of where they came from.
        if (location.key === 'default') {
          navigate(from, { replace: true });
        } else {
          navigate(-1);
        }
      }}
      // Both exits replace the wizard entry rather than stacking on top of it. The
      // sag behind them is gone - deleted, or moved on to the report - so leaving it
      // in history is what let browser-back land on a dead-job error card.
      onDone={() => navigate(from, { replace: true })}
      onGoToReport={(jobId) => navigate(`/app/completed/${jobId}`, { replace: true, state: { from } })}
    />
  );
};