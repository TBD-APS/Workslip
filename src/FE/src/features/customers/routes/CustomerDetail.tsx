import { useNavigate, useParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, Clock, Mail, MapPin, MoreHorizontal, Phone, Users } from 'lucide-react';
import { useGetApiCustomersId } from '../../../api/generated/customers/customers';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import { useCustomerActions } from '../components/CustomerActions';
import type { CustomerListItemViewModel } from '../../../api/generated/models';


export const CustomerDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const query = useGetApiCustomersId(id!);
  const customer = query.data;

  const listItems: CustomerListItemViewModel[] = customer
    ? [{ id: customer.id, name: customer.name, address: customer.address, email: customer.email, contactPerson: customer.contactPerson, phone: customer.phone, jobCount: customer.jobCount }]
    : [];

  const {
    toggleActionMenu,
    openActionMenu,
    ActionMenuPortal,
    DeleteDialog,
  } = useCustomerActions({
    customers: listItems,
    onEditCustomer: (customer) => navigate(`/app/customers/${customer.id}/edit`),
    onDeletedCustomer: () => navigate('/app/customers'),
  });

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="detail-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
      </div>
    );
  }

  if (query.isError || !customer) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente kundeoplysninger.</p>
          <button className="btn btn-primary" onClick={() => navigate('/app/customers')}>
            Tilbage til kunder
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div style={{ flex: 1 }}>
          <h2>{customer.name}</h2>
          <p className="subtitle">{customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}</p>
        </div>
        <div className="worksheet-actions-menu-root">
          <button
            type="button"
            className="btn-icon"
            onClick={(event) => toggleActionMenu(event, customer.id)}
            aria-label="More options for customer"
            aria-expanded={openActionMenu?.customerId === customer.id}
            title="Handlinger"
          >
            <MoreHorizontal size={18} />
          </button>
        </div>
      </div>

      <section className="detail-section">
          <div className="customer-detail-info">
            {customer.address && (
              <div className="detail-row">
                <MapPin size={16} />
                <span>{customer.address}</span>
              </div>
            )}
            {customer.email && (
              <div className="detail-row">
                <Mail size={16} />
                <span>{customer.email}</span>
              </div>
            )}
            {customer.contactPerson && (
              <div className="detail-row">
                <Users size={16} />
                <span>{customer.contactPerson}</span>
              </div>
            )}
            {customer.phone && (
              <div className="detail-row">
                <Phone size={16} />
                <span>{customer.phone}</span>
              </div>
            )}
          </div>
        </section>

      <div className="job-list">
        {customer.jobs.map((job) => (
          <button
          key={job.id}
            className="job-card"
            onClick={() => navigate(`/app/completed/${job.id}`, { state: { from: `/app/customers/${customer.id}` } })}
            type="button"
          >
            <div className="job-card-top">
              <div>
                <span className="job-number">
                  Sag-{job.reportNumber}
                </span>
              </div>
              <span className={`status-badge status-${job.status.toLowerCase()}`}>
                {formatJobStatus(job.status)}
              </span>
            </div>
            <div className="job-card-body">
              {job.contactPerson && (
                <span className="meta-item">
                  <Users size={14} />
                  <span>{job.contactPerson}</span>
                </span>
              )}
              {job.contactPhone && (
                <span className="meta-item">
                  <Phone size={14} />
                  <span>{job.contactPhone}</span>
                </span>
              )}
            </div>
            <div className="job-card-meta">
              <span className="meta-item">
                <Clock size={14} />
                <span className='meta-item'>Sidst opdateret: {formatDateLong(job.updatedAt)}</span>
              </span>
            </div>
          </button>
        ))}

        {customer.jobs.length === 0 && (
          <div className="empty-state">
            <p>Ingen sager for denne kunde.</p>
          </div>
        )}
      </div>

      {ActionMenuPortal}
      {DeleteDialog}
    </div>
  );
};
