import { useQuery } from '@tanstack/react-query';
import {
  ArrowRight,
  BarChart3,
  FileText,
  Heart,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { JobStatus, type JobListItemViewModel } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { apiClient } from '../../../lib/axios';
import { formatDateTimeShort } from '../../../lib/formatDate';
import { ROLES } from '../../../providers/permissions/roles';
import { useHasRole } from '../../../providers/permissions/usePermissions';
import { listDocuments } from '../../docs/docsApi';
import { JobCard } from '../../../components/JobCard';
import { getApiCustomersFavorite } from '../../../api/generated/customers/customers';
import './Overview.css';
import './Overview.dashboard-inspiration.css';

type JobOverviewResponse = {
  activeCount: number;
  inReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  recentJobs: JobListItemViewModel[];
};

const REFRESH_INTERVAL_MS = 30_000;

const fetchOverview = async () =>
  (await apiClient.get('/api/jobs/overview')) as unknown as JobOverviewResponse;

const getJobPath = (job: JobListItemViewModel) =>
  job.status === JobStatus.InReview || job.status === JobStatus.Approved
    ? `/app/completed/${job.id}`
    : `/app/job/${job.id}`;

export const Overview = () => {
  const navigate = useNavigate();
  const isAdmin = useHasRole(ROLES.Admin);

  const overviewQuery = useQuery({
    queryKey: ['/api/jobs/overview'],
    queryFn: fetchOverview,
    refetchInterval: REFRESH_INTERVAL_MS,
    refetchOnWindowFocus: true,
  });

  const favoritesQuery = useQuery({
    queryKey: ['overview', 'favorite-customers'],
    queryFn: () => getApiCustomersFavorite({ limit: 5 }),
    enabled: isAdmin,
    refetchInterval: REFRESH_INTERVAL_MS,
    refetchOnWindowFocus: true,
  });

  const documentsQuery = useQuery({
    queryKey: ['overview', 'recent-documents'],
    queryFn: () => listDocuments({ limit: 5, offset: 0 }),
    enabled: isAdmin,
    refetchInterval: REFRESH_INTERVAL_MS,
    refetchOnWindowFocus: true,
  });

  if (overviewQuery.isError) {
    return (
      <div className="page-container overview-page">
        <ErrorState
          message="Kunne ikke hente overblikket."
          onRetry={() => void overviewQuery.refetch()}
        />
      </div>
    );
  }

  const overview = overviewQuery.data;

  const recentJobs = overview?.recentJobs.slice(0, 5) ?? [];
  const favoriteCustomers = favoritesQuery.data ?? [];
  const recentDocuments = documentsQuery.data?.items ?? [];

  const recentJobsSection = (
    <section className="overview-recent-card" aria-labelledby="recent-jobs-heading">
      <div className="overview-section-header">
        <div>
          <h3 id="recent-jobs-heading">Seneste sager</h3>
          <p>De senest opdaterede sager.</p>
        </div>
        <button
          type="button"
          className="overview-text-link"
          onClick={() => navigate('/app')}
        >
          Se alle
        </button>
      </div>

      {overviewQuery.isPending ? (
        <div className="overview-recent-list" aria-label="Indlæser seneste sager">
          {Array.from({ length: 4 }).map((_, index) => (
            <div
              className="overview-recent-row overview-recent-row--skeleton"
              key={index}
              aria-hidden="true"
            >
              <span className="skeleton" />
              <span className="skeleton" />
              <span className="skeleton" />
            </div>
          ))}
        </div>
      ) : recentJobs.length ? (
        <div className="job-list overview-recent-list">
          {recentJobs.map((job) => (
            <JobCard
              key={job.id}
              id={job.id}
              reportNumber={job.reportNumber}
              status={job.status}
              customerName={job.customer?.name}
              taskDescription={job.taskDescription}
              jobType={job.jobType}
              address={job.destinationAddress || job.customer?.address}
              installationTypes={job.installationTypes}
              totalHours={job.totalHours}
              assignedUsers={job.assignedUsers}
              updatedAt={job.updatedAt}
              isSeen={job.isSeen}
              isNewRejection={job.isNewRejection}
              showUnassigned
              onOpen={() => navigate(getJobPath(job), { state: { from: '/app/overblik' } })}
            />
          ))}
        </div>
      ) : (
        <div className="overview-empty-state">
          <p>Der er ingen sager endnu.</p>
        </div>
      )}
    </section>
  );

  return (
    <div className="page-container overview-page">
      <div className="page-header overview-header">
        <div>
          <h2>Overblik</h2>
          <p>Live virksomhedsdata og de seneste aktiviteter.</p>
        </div>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() => navigate('/app')}
        >
          Se alle sager <ArrowRight size={16} aria-hidden="true" />
        </button>
      </div>

      {isAdmin ? (
        <>
          {recentJobsSection}

          <section id="overview-leader-analysis-card" className="overview-list-card" aria-labelledby="leader-analysis-heading">
            <div className="overview-section-header">
              <div>
                <h3 id="leader-analysis-heading">Lederanalyse</h3>
                <p>Nøgletal for bemanding, kvalitet og sagsflow.</p>
              </div>
              <button
                id="overview-leader-analysis-link"
                type="button"
                className="overview-text-link"
                onClick={() => navigate('/app/lederanalyse')}
              >
                Åbn analyse
              </button>
            </div>
            <button
              id="overview-leader-analysis-cta"
              type="button"
              className="overview-simple-row"
              onClick={() => navigate('/app/lederanalyse')}
            >
              <span className="overview-simple-row__icon">
                <BarChart3 size={17} aria-hidden="true" />
              </span>
              <span>
                <strong>Se driftsnøgletal</strong>
                <small>{overview ? `${overview.activeCount + overview.inReviewCount + overview.approvedCount + overview.rejectedCount} sager i alt · ${overview.inReviewCount} til gennemsyn` : 'Henter nøgletal…'}</small>
              </span>
              <ArrowRight size={16} aria-hidden="true" />
            </button>
          </section>

          <div className="overview-secondary-grid">
            <section
              className="overview-list-card"
              aria-labelledby="favorite-customers-heading"
            >
              <div className="overview-section-header">
                <div>
                  <h3 id="favorite-customers-heading">Favoritkunder</h3>
                  <p>Hurtig adgang til dine vigtigste kunder.</p>
                </div>
                <button
                  type="button"
                  className="overview-text-link"
                  onClick={() => navigate('/app/customers')}
                >
                  Se alle
                </button>
              </div>

              <div className="overview-simple-list">
                {favoritesQuery.isPending ? (
                  <div className="overview-mini-state">Henter favoritkunder…</div>
                ) : favoriteCustomers.length ? (
                  favoriteCustomers.map((customer) => (
                    <button
                      key={customer.id}
                      type="button"
                      className="overview-simple-row"
                      onClick={() => navigate(`/app/customers/${customer.id}/edit`)}
                    >
                      <span className="overview-simple-row__icon">
                        <Heart size={17} aria-hidden="true" />
                      </span>
                      <span>
                        <strong>{customer.name}</strong>
                        <small>
                          {[customer.city, customer.contactPerson]
                            .filter(Boolean)
                            .join(' · ') || 'Kunde'}
                        </small>
                      </span>
                      <ArrowRight size={16} aria-hidden="true" />
                    </button>
                  ))
                ) : (
                  <div className="overview-mini-state">Ingen favoritkunder endnu.</div>
                )}
              </div>
            </section>

            <section
              className="overview-list-card"
              aria-labelledby="recent-documents-heading"
            >
              <div className="overview-section-header">
                <div>
                  <h3 id="recent-documents-heading">Nyeste dokumenter</h3>
                  <p>Senest opdaterede dokumenter.</p>
                </div>
                <button
                  type="button"
                  className="overview-text-link"
                  onClick={() => navigate('/app/docs')}
                >
                  Se alle
                </button>
              </div>

              <div className="overview-simple-list">
                {documentsQuery.isPending ? (
                  <div className="overview-mini-state">Henter dokumenter…</div>
                ) : recentDocuments.length ? (
                  recentDocuments.map((document) => (
                    <button
                      key={document.id}
                      type="button"
                      className="overview-simple-row"
                      onClick={() => navigate(`/app/docs/${document.id}`)}
                    >
                      <span className="overview-simple-row__icon">
                        <FileText size={17} aria-hidden="true" />
                      </span>
                      <span>
                        <strong>{document.title}</strong>
                        <small>{formatDateTimeShort(document.updatedAt)}</small>
                      </span>
                      <ArrowRight size={16} aria-hidden="true" />
                    </button>
                  ))
                ) : (
                  <div className="overview-mini-state">Ingen dokumenter endnu.</div>
                )}
              </div>
            </section>
          </div>
        </>
      ) : (
        recentJobsSection
      )}
    </div>
  );
};
