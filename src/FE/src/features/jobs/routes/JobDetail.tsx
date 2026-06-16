import { useEffect, useRef } from 'react';
import { useLocation, useParams, useNavigate } from 'react-router-dom';
import { JobDetailsPage } from '../components/JobDetails';
import { useJobDetails } from '../hooks/useJobDetails';

export const JobDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | undefined)?.from ?? '/app';
  const details = useJobDetails(id);
  const initialLoadDone = useRef(false);

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
