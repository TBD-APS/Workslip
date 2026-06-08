import { useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, Mail, Shield } from 'lucide-react';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { getResponseData } from '../../../lib/unwrapResponse';

type UserViewModel = {
  id: string;
  displayName: string;
  email: string;
  role: string;
};

type UserListViewModel = {
  users: UserViewModel[];
  total: number;
};

const SkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-name" style={{ width: '60%' }} />
    </div>
    <div className="skeleton skeleton-address" style={{ width: '40%' }} />
    <div className="skeleton skeleton-tag" style={{ width: '30%' }} />
  </div>
);

export const UserList = () => {
  const navigate = useNavigate();
  const query = useGetApiUsers();
  const data = getResponseData<UserListViewModel>(query.data);
  const users = data?.users ?? [];

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="page-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
        <div className="job-list">
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </div>
      </div>
    );
  }

  if (query.isError) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente brugere. Prøv igen.</p>
          <button className="btn btn-primary" onClick={() => query.refetch()}>
            Prøv igen
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Folk</h2>
        <p className="subtitle">{users.length} {users.length === 1 ? 'bruger' : 'brugere'}</p>
      </div>

      <div className="job-list">
        {users.map((user) => (
          <button
            key={user.id}
            className="job-card"
            onClick={() => navigate(`/app/users/${user.id}`)}
            type="button"
          >
            <div className="job-card-top">
              <div>
                <h3 className="job-customer">{user.displayName}</h3>
              </div>
            </div>

            <div className="job-card-meta">
              <span className="meta-item">
                <Mail size={14} />
                <span>{user.email}</span>
              </span>
              <span className="meta-item">
                <Shield size={14} />
                <span>{user.role}</span>
              </span>
            </div>

            <div className="job-card-footer">
              <span />
              <span className="btn-icon" aria-label="Se bruger">
                <ChevronRight size={20} />
              </span>
            </div>
          </button>
        ))}

        {users.length === 0 && (
          <div className="empty-state">
            <p>Ingen brugere fundet.</p>
          </div>
        )}
      </div>
    </div>
  );
};
