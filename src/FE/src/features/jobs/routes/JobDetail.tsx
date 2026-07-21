import { useEffect, useRef } from 'react';
import { useLocation, useParams, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { JobDetailsPage } from '../components/JobDetails';
import { useJobDetails } from '../hooks/useJobDetails';
import { markJobAsSeen } from '../utils/markJobSeen';

export const JobDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const from = (location.state as { from?: string } | undefined)?.from ?? '/app';
  const details = useJobDetails(id);
  const initialLoadDone = useRef(false);

  useEffect(() => {
    if (!id) return;
    markJobAsSeen(id, queryClient);
  }, [id]);

  useEffect(() => {
    if (!details.job || initialLoadDone.current) return;
    initialLoadDone.current = true;

    const el = document.querySelector<HTMLElement>('.app-shell');
    if (!el) return;

    el.scrollTo(0, 0);
    requestAnimationFrame(() => el.scrollTop = 0);
  }, [details.job]);

  return (
    <JobDetailsPage
      details={details}
      onBack={() => navigate(-1)}
      onDone={() => navigate(from)}
    />
  );
};
