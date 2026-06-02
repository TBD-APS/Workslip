import { useParams, useNavigate } from 'react-router-dom';
import { JobDetailsPage } from '../components/JobDetails';
import { useJobDetails } from '../hooks/useJobDetails';

export const JobDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const details = useJobDetails(id);

  return (
    <JobDetailsPage
      details={details}
      onBack={() => navigate('/app')}
      onDone={() => navigate('/app')}
    />
  );
};
