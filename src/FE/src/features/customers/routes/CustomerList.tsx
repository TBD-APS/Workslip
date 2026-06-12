import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, Building2, ChevronRight, Mail, Users, MapPin, Phone } from 'lucide-react';
import { useGetApiCustomers } from '../../../api/generated/customers/customers';
import { SearchBar } from '../../../components/filters/SearchBar';
import { useSearch } from '../../../hooks/useSearch';

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
  const [search, setSearch] = useState('');
  const query = useGetApiCustomers();
  const data = query.data;
  const sorted = (data ?? []).sort((a, b) => Number(b.jobCount) - Number(a.jobCount));
  const customers = useSearch(sorted, search, (c, term) =>
    [c.name, c.address, c.email, c.contactPerson, c.phone].some((v) => v?.toLowerCase().includes(term)),
  );

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

      <SearchBar value={search} onChange={setSearch} placeholder="Søg kunder..." />
      <div className="search-bar-spacer" />

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
                  <Phone size={14} />
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