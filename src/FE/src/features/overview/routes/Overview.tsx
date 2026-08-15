import { useQuery } from '@tanstack/react-query';
import { ArrowRight, CheckCircle2, CircleDot, Clock3, XCircle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { JobStatus, type JobListItemViewModel } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { apiClient } from '../../../lib/axios';
import { formatDateTimeShort } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import './Overview.css';

const RECENT_JOB_LIMIT = 6;
const ALL_STATUSES = [JobStatus.Draft, JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected];

type JobsResponse = {
  items: JobListItemViewModel[];
  totalCount: number;
};

const fetchJobs = async (status: JobStatus[], limit: number, sortBy?: string, sortDirection?: string) => {
  const data = await apiClient.get('/api/jobs', {
    params: {
      status,
      limit,
      offset: 0,
      sortBy,
      sortDirection,
    },
  });

  return data as unknown as JobsResponse;
};

const getJobPath = (job: JobListItemViewModel) =>
  job.status === JobStatus.InReview || job.status === JobStatus.Approved
    ? `/app/completed/${job.id}`
    : `/app/job/${job.id}`;

export const Overview = () => {
  const navigate = useNavigate();

  const activeQuery = useQuery({ queryKey: ['/api/jobs', 'overview-count', JobStatus.Draft], queryFn: () => fetchJobs([JobStatus.Draft], 1) });
  const reviewQuery = useQuery({ queryKey: ['/api/jobs', 'overview-count', JobStatus.InReview], queryFn: () => fetchJobs([JobStatus.InReview], 1) });
  const approvedQuery = useQuery({ queryKey: ['/api/jobs', 'overview-count', JobStatus.Approved], queryFn: () => fetchJobs([JobStatus.Approved], 1) });
  const rejectedQuery = useQuery({ queryKey: ['/api/jobs', 'overview-count', JobStatus.Rejected], queryFn: () => fetchJobs([JobStatus.Rejected], 1) });
  const recentQuery = useQuery({ queryKey: ['/api/jobs', 'overview-recent'], queryFn: () => fetchJobs(ALL_STATUSES, RECENT_JOB_LIMIT, 'updatedAt', 'desc') });

  const queries = [activeQuery, reviewQuery, approvedQuery, rejectedQuery, recentQuery];
  const isLoading = queries.some((query) => query.isPending);
  const isError = queries.some((query) => query.isError);
  const refetchAll = () => { void Promise.all(queries.map((query) => query.refetch())); };

  if (isError && !recentQuery.data) {
    return <div className="page-container overview-page"><ErrorState message="Kunne ikke hente overblikket." onRetry={refetchAll} /></div>;
  }

  const statusCards = [
    { status: JobStatus.Draft, label: 'Aktive sager', count: activeQuery.data?.totalCount, icon: <CircleDot size={22} aria-hidden="true" />, className: 'overview-status-card--active' },
    { status: JobStatus.InReview, label: 'Til gennemsyn', count: reviewQuery.data?.totalCount, icon: <Clock3 size={22} aria-hidden="true" />, className: 'overview-status-card--review' },
    { status: JobStatus.Approved, label: 'Godkendte sager', count: approvedQuery.data?.totalCount, icon: <CheckCircle2 size={22} aria-hidden="true" />, className: 'overview-status-card--approved' },
  ];

  return (
    <div className="page-container overview-page">
      <div className="page-header overview-header">
        <div><h2>Overblik</h2><p>Se status på dine sager og fortsæt hurtigt, hvor du slap.</p></div>
        <button type="button" className="btn btn-secondary" onClick={() => navigate('/app')}>Se alle sager <ArrowRight size={16} aria-hidden="true" /></button>
      </div>

      <section className="overview-status-grid" aria-label="Sagsstatus">
        {statusCards.map((card) => (
          <button key={card.status} type="button" className={`overview-status-card ${card.className}`} onClick={() => navigate('/app')}>
            <span className="overview-status-card__icon">{card.icon}</span>
            <span className="overview-status-card__content"><span className="overview-status-card__count">{isLoading && card.count === undefined ? '–' : card.count ?? 0}</span><span className="overview-status-card__label">{card.label}</span></span>
            <ArrowRight className="overview-status-card__arrow" size={18} aria-hidden="true" />
          </button>
        ))}
      </section>

      <section className="overview-recent-card" aria-labelledby="recent-jobs-heading">
        <div className="overview-section-header">
          <div><h3 id="recent-jobs-heading">Seneste sager</h3><p>De senest opdaterede sager.</p></div>
          {(rejectedQuery.data?.totalCount ?? 0) > 0 && (
            <button type="button" className="overview-rejected-link" onClick={() => navigate('/app')}>
              <XCircle size={16} aria-hidden="true" />{rejectedQuery.data?.totalCount} afvist{rejectedQuery.data?.totalCount === 1 ? '' : 'e'}
            </button>
          )}
        </div>

        {recentQuery.isPending ? (
          <div className="overview-recent-list" aria-label="Indlæser seneste sager">
            {Array.from({ length: 4 }).map((_, index) => <div className="overview-recent-row overview-recent-row--skeleton" key={index} aria-hidden="true"><span className="skeleton" /><span className="skeleton" /><span className="skeleton" /></div>)}
          </div>
        ) : recentQuery.data?.items.length ? (
          <div className="overview-recent-list">
            {recentQuery.data.items.map((job) => {
              const customerName = job.customer?.name || 'Kunde ikke angivet';
              const address = job.destinationAddress || job.customer?.address;
              return (
                <button type="button" className="overview-recent-row" key={job.id} onClick={() => navigate(getJobPath(job), { state: { from: '/app/overblik' } })}>
                  <span className="overview-recent-row__main"><strong>SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}</strong><span>{customerName}</span>{address && <small>{address}</small>}</span>
                  <span className={`status-badge status-${job.status.toLowerCase()}`}>{formatJobStatus(job.status)}</span>
                  <span className="overview-recent-row__updated">{job.updatedAt ? formatDateTimeShort(job.updatedAt) : '–'}</span>
                  <ArrowRight size={17} aria-hidden="true" />
                </button>
              );
            })}
          </div>
        ) : (
          <div className="overview-empty-state"><p>Der er ingen sager endnu.</p><button type="button" className="btn btn-secondary" onClick={() => navigate('/app')}>Gå til sager</button></div>
        )}
      </section>
    </div>
  );
};
