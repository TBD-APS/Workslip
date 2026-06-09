import { useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { JobDetailsPage } from '../components/JobDetails';
import { useJobDetails } from '../hooks/useJobDetails';

export const JobDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const details = useJobDetails(id);
  const initialLoadDone = useRef(false);

  useEffect(() => {
    if (!details.job || initialLoadDone.current) return;
    initialLoadDone.current = true;

    const el = document.querySelector<HTMLElement>('.app-content');
    if (!el) return;

    el.scrollTo(0, 0);
    requestAnimationFrame(() => el.scrollTop = 0);
  }, [details.job]);

  return (
    <JobDetailsPage
      details={details}
      onBack={() => navigate('/app')}
      onDone={() => navigate('/app')}
    />
  );
};
