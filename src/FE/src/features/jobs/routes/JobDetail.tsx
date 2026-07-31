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
  const { setCurrentStep } = details;
  const normalizedRejectedJobIdRef = useRef<string | null>(null);

  useScrollRestore(`job:${id}`);

  useEffect(() => {
    if (!id || !jobStatus) return;
    if (isAdmin && jobStatus === JobStatus.Rejected) return;

    markJobAsSeen(id, queryClient);
  }, [id, jobStatus, isAdmin, queryClient]);

  useEffect(() => {
    if (!id || !loadedJobId || loadedJobId !== id) {
      normalizedRejectedJobIdRef.current = null;
      return;
    }

    if (jobStatus !== JobStatus.Rejected || isAdmin) {
      normalizedRejectedJobIdRef.current = null;
      return;
    }

    if (normalizedRejectedJobIdRef.current === loadedJobId) return;
    normalizedRejectedJobIdRef.current = loadedJobId;

    setCurrentStep((step) => (step === 0 ? step : 0));
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  }, [id, loadedJobId, jobStatus, setCurrentStep, isAdmin]);

  if (id && details.job?.jobType === 'Diverse') {
    return <Navigate to={`/app/completed/${id}`} replace state={{ from }} />;
  }

  if (id && isAdmin && jobStatus === JobStatus.Rejected) {
    return <Navigate to={`/app/completed/${id}`} replace state={{ from }} />;
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
