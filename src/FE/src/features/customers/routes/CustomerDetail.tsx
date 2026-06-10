import { useParams, useNavigate } from 'react-router-dom';
import { AlertCircle, ArrowLeft, Clock, Mail, MapPin, Timer, Users } from 'lucide-react';
import { useGetApiCustomersId } from '../../../api/generated/customers/customers';
import { formatDate } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';

function formatJobNumber(reportNumber: string | null | undefined, id: string) {
  const prefix = reportNumber ?? id.slice(0, 4);
  return `SAG-${prefix.toUpperCase()}`;
}

export const CustomerDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const query = useGetApiCustomersId(id!);
  const customer = query.data;

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="page-header">
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
      <div className="page-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div>
          <h2>{customer.name}</h2>
          <p className="subtitle">{customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}</p>
        </div>
      </div>

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
            <Timer size={16} />
            <span>{customer.phone}</span>
          </div>
        )}
      </div>

      <h3 className="section-title">Sager for denne kunde</h3>

      <div className="job-list">
        {customer.jobs.map((job) => (
          <button
            key={job.id}
            className="job-card"
            onClick={() => navigate(`/app/completed/${job.id}`)}
            type="button"
          >
            <div className="job-card-top">
              <div>
                <span className="job-number">
                  {formatJobNumber(job.reportNumber, job.id)}
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
                  <Timer size={14} />
                  <span>{job.contactPhone}</span>
                </span>
              )}
            </div>
            <div className="job-card-footer">
              <span className="meta-item">
                <Clock size={14} />
                <span>Sidst opdateret: {formatDate(job.updatedAt)}</span>
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
    </div>
  );
};