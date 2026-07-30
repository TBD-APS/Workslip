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

  useScrollRestore(`job:${id}`);

  useEffect(() => {
    if (!id) return;
    markJobAsSeen(id, queryClient);
    if (details.job?.status === JobStatus.Rejected) {
      markJobAsSeen(id, queryClient, 'RejectedAssignment');
    }
  }, [id, details.job?.status, queryClient]);

  useEffect(() => {
    if (details.job?.status !== JobStatus.Rejected || isAdmin) return;

    if (details.currentStep !== 0) {
      details.setCurrentStep(0);
    }
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  }, [details.job?.status, details.currentStep, details.setCurrentStep, isAdmin]);

  if (id && isAdmin && details.job?.status === JobStatus.Rejected) {
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
