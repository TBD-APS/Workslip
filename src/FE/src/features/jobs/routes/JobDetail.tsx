import { useEffect } from 'react';
import { useLocation, useParams, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { JobDetailsPage } from '../components/JobDetails';
import { useJobDetails } from '../hooks/useJobDetails';
import { markJobAsSeen } from '../utils/markJobSeen';
import { useScrollRestore } from '../../../hooks/useScrollRestore';
import { JobStatus } from '../../../api/generated/models';

export const JobDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const from = (location.state as { from?: string } | undefined)?.from ?? '/app';
  const details = useJobDetails(id);

  useScrollRestore(`job:${id}`);

  useEffect(() => {
    if (!id) return;
    markJobAsSeen(id, queryClient);
    if (details.job?.status === JobStatus.Rejected) {
      markJobAsSeen(id, queryClient, 'RejectedAssignment');
    }
  }, [id, details.job?.status]);

  useEffect(() => {
    if (details.job?.status !== JobStatus.Rejected) return;

    if (details.currentStep !== 0) {
      details.setCurrentStep(0);
    }
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  }, [details.job?.status, details.currentStep, details.setCurrentStep]);

  return (
    <JobDetailsPage
      details={details}
      onBack={() => navigate(-1)}
      onDone={() => navigate(from)}
      onGoToReport={(jobId) => navigate(`/app/completed/${jobId}`)}
    />
  );
};
