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
  const loadedJobId = details.job?.id;
  const { currentStep, setCurrentStep } = details;

  useScrollRestore(`job:${id}`);

  useEffect(() => {
    if (!id || !jobStatus) return;
    if (isAdmin && jobStatus === JobStatus.Rejected) return;

    markJobAsSeen(id, queryClient);
  }, [id, jobStatus, isAdmin, queryClient]);

  useEffect(() => {
    if (!id || !loadedJobId || loadedJobId !== id) return;

    // Rejected jobs are corrected by the assignee; Reopened jobs are correction
    // states for both roles. Keep both at Sagsdetaljer even if the generic
    // worksheet auto-navigation resolves later and tries to move the wizard.
    const shouldStayOnCorrectionOverview =
      jobStatus === JobStatus.Reopened ||
      (jobStatus === JobStatus.Rejected && !isAdmin);

    if (!shouldStayOnCorrectionOverview || currentStep === 0) return;

    setCurrentStep(0);
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  }, [id, loadedJobId, jobStatus, currentStep, setCurrentStep, isAdmin]);

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