import { useNavigate } from 'react-router-dom';
import { AlertCircle, Building2, ChevronRight, Mail, Timer, Users, MapPin } from 'lucide-react';
import { useGetApiCustomers } from '../../../api/generated/customers/customers';

const SkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-name" style={{ width: '60%' }} />
    </div>
    <div className="skeleton skeleton-address" style={{ width: '40%' }} />
    <div className="skeleton skeleton-tag" style={{ width: '30%' }} />
  </div>
);

export const CustomerList = () => {
  const navigate = useNavigate();
  const query = useGetApiCustomers();
  const data = query.data;
  const customers = data ?? [];

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
          <p>Kunne ikke hente kunder. Prøv igen.</p>
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
        <h2>Kunder</h2>
        <p className="subtitle">{customers.length} {customers.length === 1 ? 'kunde' : 'kunder'}</p>
      </div>

      <div className="job-list">
        {customers.map((customer) => (
          <button
            key={customer.id}
            className="job-card"
            onClick={() => navigate(`/app/customers/${customer.id}`)}
            type="button"
          >
            <div className="job-card-top">
              <div>
                <Building2 size={20} style={{ marginRight: '0.5rem', verticalAlign: 'middle' }} />
                <h3 className="job-customer" style={{ display: 'inline' }}>{customer.name}</h3>
              </div>
              <span className="meta-item">
                <Users size={14} />
                <span>{customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}</span>
              </span>
            </div>

            <div className="job-card-body">
              {customer.address && (
                <span className="meta-item">
                  <MapPin size={14} />
                  <span>{customer.address}</span>
                </span>
              )}
              {customer.email && (
                <span className="meta-item">
                  <Mail size={14} />
                  <span>{customer.email}</span>
                </span>
              )}
              {customer.contactPerson && (
                <span className="meta-item">
                  <Users size={14} />
                  <span>{customer.contactPerson}</span>
                </span>
              )}
              {customer.phone && (
                <span className="meta-item">
                  <Timer size={14} />
                  <span>{customer.phone}</span>
                </span>
              )}
            </div>

            <div className="job-card-footer">
              <span />
              <span className="btn-icon" aria-label="Se kunde">
                <ChevronRight size={20} />
              </span>
            </div>
          </button>
        ))}

        {customers.length === 0 && (
          <div className="empty-state">
            <p>Ingen kunder fundet.</p>
          </div>
        )}
      </div>
    </div>
  );
};