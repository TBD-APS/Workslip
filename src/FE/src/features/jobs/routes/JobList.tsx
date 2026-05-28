import { useQuery } from '@tanstack/react-query';
import { MapPin, Clock, ChevronRight, AlertCircle } from 'lucide-react';

// Move to api folder later
const fetchJobs = async (): Promise<any[]> => {
  // Simulate API call for MVP
  // return apiClient.get('/jobs');
  
  return new Promise((resolve) => {
    setTimeout(() => {
      resolve([
        {
          id: 'JOB-2034',
          customer: 'A.P. Møller Skolen',
          address: 'Fælledvej 12, Slesvig',
          type: 'Vandinstallation',
          status: 'Assigned',
          date: 'I dag, 08:00',
          urgent: true
        },
        {
          id: 'JOB-2035',
          customer: 'Privat: Jens Jensen',
          address: 'Skovvej 4, Hillerød',
          type: 'Gaseftersyn',
          status: 'Draft',
          date: 'I morgen, 10:00',
          urgent: false
        },
        {
          id: 'JOB-2031',
          customer: 'Boligselskabet Sjælland',
          address: 'Parkvej 2A, Roskilde',
          type: 'Varmeanlæg Service',
          status: 'Submitted',
          date: 'I går',
          urgent: false
        }
      ]);
    }, 800);
  });
};

export const JobList = () => {
  const { data: jobs, isLoading, error } = useQuery({
    queryKey: ['jobs'],
    queryFn: fetchJobs,
  });

  if (isLoading) {
    return <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--text-secondary)' }}>Henter dine jobs...</div>;
  }

  if (error) {
    return <div style={{ color: '#ef4444' }}>Kunne ikke hente jobs.</div>;
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Dine opgaver</h2>
        <p className="subtitle">Du har {jobs?.length || 0} afventende jobs</p>
      </div>

      <div className="job-list">
        {jobs?.map((job: any) => (
          <div key={job.id} className="job-card">
            <div className="job-card-header">
              <span className={`status-badge status-${job.status.toLowerCase()}`}>
                {job.status}
              </span>
              <span className="job-id">{job.id}</span>
            </div>
            
            <h3 className="job-customer">{job.customer}</h3>
            
            <div className="job-details">
              <div className="job-detail-item">
                <MapPin size={14} />
                <span>{job.address}</span>
              </div>
              <div className="job-detail-item">
                <Clock size={14} />
                <span className={job.urgent ? 'text-urgent' : ''}>
                  {job.date}
                  {job.urgent && <AlertCircle size={14} style={{ marginLeft: '4px', display: 'inline' }} />}
                </span>
              </div>
            </div>

            <div className="job-card-footer">
              <span className="job-type">{job.type}</span>
              <button className="btn-icon">
                <ChevronRight size={20} />
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
