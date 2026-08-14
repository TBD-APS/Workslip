import { useParams } from 'react-router-dom';
import { UserRateCard } from '../components/UserRateCard';
import { UserDetail } from './UserDetail';

export function UserDetailWithRate() {
  const { id } = useParams<{ id: string }>();

  return (
    <>
      <UserDetail />
      {id && (
        <div className="page-container">
          <UserRateCard userId={id} />
        </div>
      )}
    </>
  );
}
