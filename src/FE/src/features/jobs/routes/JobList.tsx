import { useQuery } from '@tanstack/react-query';
import { MapPin, Clock, ChevronRight, AlertCircle } from 'lucide-react';
import { getJobs } from '../api/getJobs';
import type { JobListItemViewModel } from '../types';

export const JobList = () => {
  const { data: jobs, isLoading, error } = useQuery<JobListItemViewModel[]>({
    queryKey: ['jobs'],
    queryFn: getJobs,
  });

  if (isLoading) {
    return <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--text-secondary)' }}>Henter dine jobs...</div>;
  }

  if (error) {
    return <div style={{ color: '#ef4444', textAlign: 'center', padding: '2rem' }}>Kunne ikke hente jobs. Sørg for at du er logget ind.</div>;
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Dine opgaver</h2>
        <p className="subtitle">Du har {jobs?.length || 0} registrerede jobs</p>
      </div>

      <div className="job-list">
        {jobs?.map((job) => (
          <div key={job.id} className="job-card">
            <div className="job-card-header">
              <span className={`status-badge status-${job.status.toLowerCase()}`}>
                {job.status}
              </span>
              <span className="job-id">{job.reportNumber || 'Mangler ID'}</span>
            </div>
            
            <h3 className="job-customer">{job.customer?.name || 'Ukendt kunde'}</h3>
            
            <div className="job-details">
              <div className="job-detail-item">
                <MapPin size={14} />
                <span>{job.customer?.address || 'Ingen adresse angivet'}</span>
              </div>
              <div className="job-detail-item">
                <Clock size={14} />
                <span className={job.status === 'Draft' ? 'text-urgent' : ''}>
                  {job.reportDate ? new Date(job.reportDate).toLocaleDateString('da-DK') : 'Ingen dato'}
                  {job.status === 'Draft' && <AlertCircle size={14} style={{ marginLeft: '4px', display: 'inline' }} />}
                </span>
              </div>
            </div>

            <div className="job-card-footer">
              <span className="job-type">{job.workKind || 'Opgave'}</span>
              <button className="btn-icon">
                <ChevronRight size={20} />
              </button>
            </div>
          </div>
        ))}
        
        {jobs?.length === 0 && (
          <div style={{ textAlign: 'center', padding: '3rem 1rem', color: 'var(--text-secondary)' }}>
            <p>Du har ingen jobs endnu.</p>
          </div>
        )}
      </div>
    </div>
  );
};
