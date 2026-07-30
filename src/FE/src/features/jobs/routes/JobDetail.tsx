import { useEffect } from 'react';
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
  const { currentStep, setCurrentStep } = details;

  useScrollRestore(`job:${id}`);

  useEffect(() => {
    if (!id || !jobStatus) return;
    if (isAdmin && jobStatus === JobStatus.Rejected) return;

    markJobAsSeen(id, queryClient);
    if (jobStatus === JobStatus.Rejected) {
      markJobAsSeen(id, queryClient, 'RejectedAssignment');
    }
  }, [id, jobStatus, isAdmin, queryClient]);

  useEffect(() => {
    if (jobStatus !== JobStatus.Rejected || isAdmin) return;

    if (currentStep !== 0) {
      setCurrentStep(0);
    }
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  }, [jobStatus, currentStep, setCurrentStep, isAdmin]);

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
