import { useQuery } from '@tanstack/react-query';
import { ArrowRight, CheckCircle2, CircleDot, Clock3, XCircle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { JobStatus } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { activateStatusFilter } from '../../../components/filters/StatusFilter';
import { apiClient } from '../../../lib/axios';
import { formatDateTimeShort } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import './Overview.css';

type JobOverviewRecentJob = {
  id: string;
  reportNumber?: string | null;
  status: JobStatus;
  customerName?: string | null;
  customerNumber?: string | null;
  address?: string | null;
  updatedAt: string;
};

type JobOverviewResponse = {
  activeCount: number;
  inReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  recentJobs: JobOverviewRecentJob[];
};

const fetchOverview = async () => {
  const data = await apiClient.get('/api/jobs/overview');
  return data as unknown as JobOverviewResponse;
};

const getJobPath = (job: JobOverviewRecentJob) =>
  job.status === JobStatus.InReview || job.status === JobStatus.Approved
    ? `/app/completed/${job.id}`
    : `/app/job/${job.id}`;

const getStatusListPath = (status: JobStatus) => `/app?status=${encodeURIComponent(status)}`;

export const Overview = () => {
  const navigate = useNavigate();
  const overviewQuery = useQuery({
    queryKey: ['/api/jobs/overview'],
    queryFn: fetchOverview,
  });

  const navigateToStatus = (status: JobStatus) => {
    activateStatusFilter('mine-jobs', [status]);
    navigate(getStatusListPath(status));
  };

  if (overviewQuery.isError) {
    return (
      <div className="page-container overview-page">
        <ErrorState message="Kunne ikke hente overblikket." onRetry={() => void overviewQuery.refetch()} />
      </div>
    );
  }

  const overview = overviewQuery.data;
  const statusCards = [
    {
      status: JobStatus.Draft,
      label: 'Aktive sager',
      count: overview?.activeCount,
      icon: <CircleDot size={22} aria-hidden="true" />,
      className: 'overview-status-card--active',
    },
    {
      status: JobStatus.InReview,
      label: 'Til gennemsyn',
      count: overview?.inReviewCount,
      icon: <Clock3 size={22} aria-hidden="true" />,
      className: 'overview-status-card--review',
    },
    {
      status: JobStatus.Approved,
      label: 'Godkendte sager',
      count: overview?.approvedCount,
      icon: <CheckCircle2 size={22} aria-hidden="true" />,
      className: 'overview-status-card--approved',
    },
  ];

  return (
    <div className="page-container overview-page">
      <div className="page-header overview-header">
        <div>
          <h2>Overblik</h2>
          <p>Se status på dine sager og fortsæt hurtigt, hvor du slap.</p>
        </div>
        <button
          type="button"
          className="btn btn-secondary overview-rejected-cta"
          onClick={() => navigateToStatus(JobStatus.Rejected)}
        >
          <XCircle size={16} aria-hidden="true" />
          Se afviste sager
          {(overview?.rejectedCount ?? 0) > 0 && <span>({overview?.rejectedCount})</span>}
        </button>
      </div>

      <section className="overview-status-grid" aria-label="Sagsstatus">
        {statusCards.map((card) => (
          <button
            key={card.status}
            type="button"
            className={`overview-status-card ${card.className}`}
            onClick={() => navigateToStatus(card.status)}
          >
            <span className="overview-status-card__icon">{card.icon}</span>
            <span className="overview-status-card__content">
              <span className="overview-status-card__count">
                {overviewQuery.isPending && card.count === undefined ? '–' : card.count ?? 0}
              </span>
              <span className="overview-status-card__label">{card.label}</span>
            </span>
            <ArrowRight className="overview-status-card__arrow" size={18} aria-hidden="true" />
          </button>
        ))}
      </section>

      <section className="overview-recent-card" aria-labelledby="recent-jobs-heading">
        <div className="overview-section-header">
          <div>
            <h3 id="recent-jobs-heading">Seneste sager</h3>
            <p>De senest opdaterede sager.</p>
          </div>
        </div>

        {overviewQuery.isPending ? (
          <div className="overview-recent-list" aria-label="Indlæser seneste sager">
            {Array.from({ length: 4 }).map((_, index) => (
              <div className="overview-recent-row overview-recent-row--skeleton" key={index} aria-hidden="true">
                <span className="skeleton" />
                <span className="skeleton" />
                <span className="skeleton" />
              </div>
            ))}
          </div>
        ) : overview?.recentJobs.length ? (
          <div className="overview-recent-list">
            {overview.recentJobs.map((job) => (
              <button
                type="button"
                className="overview-recent-row"
                key={job.id}
                onClick={() => navigate(getJobPath(job), { state: { from: '/app/overblik' } })}
              >
                <span className="overview-recent-row__main">
                  <strong>SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}</strong>
                  <span>{job.customerName || 'Kunde ikke angivet'}</span>
                  {job.customerNumber && <small>Kundenr. {job.customerNumber}</small>}
                  {job.address && <small>{job.address}</small>}
                </span>
                <span className={`status-badge status-${job.status.toLowerCase()}`}>
                  {formatJobStatus(job.status)}
                </span>
                <span className="overview-recent-row__updated">
                  {job.updatedAt ? formatDateTimeShort(job.updatedAt) : '–'}
                </span>
                <ArrowRight size={17} aria-hidden="true" />
              </button>
            ))}
          </div>
        ) : (
          <div className="overview-empty-state">
            <p>Der er ingen sager endnu.</p>
            <button type="button" className="btn btn-secondary" onClick={() => navigateToStatus(JobStatus.Draft)}>
              Gå til aktive sager
            </button>
          </div>
        )}
      </section>
    </div>
  );
};
