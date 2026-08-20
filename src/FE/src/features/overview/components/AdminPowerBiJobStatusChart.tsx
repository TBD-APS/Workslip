import { useQuery } from '@tanstack/react-query';
import { Info, Loader2, RefreshCw } from 'lucide-react';
import { apiClient } from '../../../lib/axios';

type PowerBiJobStatusResponse = {
  total: number;
  draft: number;
  inReview: number;
  approved: number;
  rejected: number;
  other: number;
  generatedAtUtc: string;
};

type Segment = {
  key: 'draft' | 'inReview' | 'approved' | 'rejected' | 'other';
  label: string;
  count: number;
};

type ChartSegment = Segment & {
  percentage: number;
  offset: number;
};

const fetchJobStatus = async () => (await apiClient.get(
  '/api/power-bi/overview/job-status',
  { skipGlobalErrorToast: true },
)) as PowerBiJobStatusResponse;

const buildSegments = (data: PowerBiJobStatusResponse): Segment[] => {
  const segments: Segment[] = [
    { key: 'draft', label: 'Aktive', count: data.draft },
    { key: 'inReview', label: 'Til gennemsyn', count: data.inReview },
    { key: 'approved', label: 'Godkendte', count: data.approved },
    { key: 'rejected', label: 'Afviste', count: data.rejected },
    { key: 'other', label: 'Øvrige', count: data.other },
  ];

  return segments.filter((segment) => segment.count > 0);
};

const buildChartSegments = (segments: Segment[], total: number): ChartSegment[] =>
  segments.map((segment, index) => {
    const percentage = total > 0 ? (segment.count / total) * 100 : 0;
    const offset = segments
      .slice(0, index)
      .reduce((sum, previous) => sum + (total > 0 ? (previous.count / total) * 100 : 0), 0);

    return { ...segment, percentage, offset };
  });

const formatUpdatedAt = (value: string) => {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Data opdateret';

  const time = new Intl.DateTimeFormat('da-DK', {
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);

  return `Data opdateret i dag kl. ${time}`;
};

export function AdminPowerBiJobStatusChart() {
  const statusQuery = useQuery({
    queryKey: ['power-bi', 'overview', 'job-status'],
    queryFn: fetchJobStatus,
    retry: false,
    staleTime: 5 * 60_000,
  });

  const data = statusQuery.data;
  const segments = data ? buildSegments(data) : [];
  const total = data?.total ?? 0;
  const chartSegments = buildChartSegments(segments, total);

  return (
    <section
      className="overview-power-bi-card"
      aria-labelledby="overview-power-bi-heading"
      data-testid="admin-power-bi-job-status"
    >
      <div className="overview-section-header overview-power-bi-header">
        <h3 id="overview-power-bi-heading">Sagsfordeling</h3>
        {statusQuery.isError && (
          <button
            type="button"
            className="btn btn-secondary overview-power-bi-retry"
            onClick={() => { void statusQuery.refetch(); }}
          >
            <RefreshCw size={16} aria-hidden="true" />
            Prøv igen
          </button>
        )}
      </div>

      {statusQuery.isPending ? (
        <div className="overview-power-bi-state" role="status">
          <Loader2 className="overview-power-bi-spinner" size={22} aria-hidden="true" />
          <span>Henter rapportdata…</span>
        </div>
      ) : statusQuery.isError ? (
        <div className="overview-power-bi-state overview-power-bi-state--error" role="alert">
          <strong>Kunne ikke hente Power BI-data</strong>
          <span>Diagrammet påvirker ikke resten af overblikssiden.</span>
        </div>
      ) : (
        <>
          <div className="overview-power-bi-content">
            <div className="overview-power-bi-donut-wrap">
              <svg
                className="overview-power-bi-donut"
                viewBox="0 0 42 42"
                role="img"
                aria-label={`Sagsfordeling. ${segments.map((segment) => `${segment.label}: ${segment.count}`).join(', ') || 'Ingen sager'}`}
              >
                <circle className="overview-power-bi-donut__track" cx="21" cy="21" r="15.9155" />
                {chartSegments.map((segment) => (
                  <circle
                    key={segment.key}
                    className={`overview-power-bi-donut__segment overview-power-bi-donut__segment--${segment.key}`}
                    cx="21"
                    cy="21"
                    r="15.9155"
                    strokeDasharray={`${segment.percentage} ${100 - segment.percentage}`}
                    strokeDashoffset={-segment.offset}
                  />
                ))}
              </svg>
              <div className="overview-power-bi-donut__center" aria-hidden="true">
                <strong>{total}</strong>
                <span>I alt</span>
              </div>
            </div>

            <ul className="overview-power-bi-legend" aria-label="Fordeling af sager">
              {segments.length > 0 ? segments.map((segment) => {
                const percentage = total > 0 ? Math.round((segment.count / total) * 100) : 0;
                return (
                  <li key={segment.key}>
                    <span className={`overview-power-bi-legend__dot overview-power-bi-legend__dot--${segment.key}`} aria-hidden="true" />
                    <span>{segment.label}</span>
                    <strong>{segment.count} ({percentage}%)</strong>
                  </li>
                );
              }) : (
                <li className="overview-power-bi-legend__empty">Der er ingen sager at vise endnu.</li>
              )}
            </ul>
          </div>
          {data?.generatedAtUtc && (
            <div className="overview-power-bi-updated">
              <Info size={15} aria-hidden="true" />
              <span>{formatUpdatedAt(data.generatedAtUtc)}</span>
            </div>
          )}
        </>
      )}
    </section>
  );
}
