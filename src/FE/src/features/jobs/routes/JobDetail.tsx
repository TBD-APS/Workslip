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
    if (!id || !loadedJobId || loadedJobId !== id || !referenceData) {
      if (!loadedJobId || loadedJobId !== id) {
        normalizedCorrectionJobIdRef.current = null;
      }
      return;
    }

    // Rejected jobs are corrected by the assignee; Reopened jobs are correction
    // states for both roles. Normalize once after reference data has resolved so
    // this runs after the generic worksheet auto-navigation decision, but do not
    // pin the user to step 0 once they deliberately continue through the wizard.
    const shouldNormalizeCorrectionStep =
      jobStatus === JobStatus.Reopened ||
      (jobStatus === JobStatus.Rejected && !isAdmin);

    if (!shouldNormalizeCorrectionStep) {
      normalizedCorrectionJobIdRef.current = null;
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
      onBack={() => navigate(-1)}
      onDone={() => navigate(from)}
      onGoToReport={(jobId) => navigate(`/app/completed/${jobId}`)}
    />
  );
};