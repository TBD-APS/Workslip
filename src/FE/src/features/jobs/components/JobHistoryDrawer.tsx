import { ChevronDown, History, Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useGetApiJobsIdHistory } from '../../../api/generated/jobs/jobs';
import type { JobHistoryResponse } from '../../../api/generated/models';
import { Drawer } from '../../../components/common/Drawer';
import {
  formatActivityDateSection,
  formatRelativeActivityTime,
  getActivityInitials,
} from '../../../components/common/activityFeed';
import '../../../components/common/ActivityFeed.css';
import './JobHistoryDrawer.css';

type JobHistoryDrawerProps = {
  jobId: string;
  isOpen: boolean;
  onClose: () => void;
};

type HistorySection = {
  label: string;
  events: JobHistoryResponse[];
};

export function JobHistoryDrawer({ jobId, isOpen, onClose }: JobHistoryDrawerProps) {
  const { data: history, isLoading, refetch } = useGetApiJobsIdHistory(jobId, undefined, {
    query: {
      enabled: isOpen,
    },
  });

  useEffect(() => {
    if (isOpen) {
      void refetch();
    }
  }, [isOpen, refetch]);

  const sections = useMemo<HistorySection[]>(() => {
    if (!history) return [];

    const grouped = new Map<string, JobHistoryResponse[]>();
    for (const event of history) {
      const label = formatActivityDateSection(event.createdAt);
      const events = grouped.get(label);
      if (events) events.push(event);
      else grouped.set(label, [event]);
    }

    return [...grouped.entries()].map(([label, events]) => ({ label, events }));
  }, [history]);

  return (
    <Drawer
      isOpen={isOpen}
      onClose={onClose}
      title="Sagsaktivitet"
      ariaLabel="Sagsaktivitet og historik"
      icon={<History size={20} />}
      className="history-drawer job-history-drawer"
    >
      <div className="job-history-overview">
        <strong>{history?.length ?? 0} {history?.length === 1 ? 'hændelse' : 'hændelser'}</strong>
        <span>Ændringer på sagen vises kronologisk med bruger, tidspunkt og feltændringer.</span>
      </div>

      {isLoading ? (
        <div className="job-history-skeleton" aria-label="Henter sagshistorik">
          <span />
          <span />
          <span />
        </div>
      ) : !history || history.length === 0 ? (
        <div className="job-history-empty">
          <History size={28} aria-hidden="true" />
          <strong>Ingen aktivitet endnu</strong>
          <span>Ændringer på sagen dukker op her.</span>
        </div>
      ) : (
        <div className="activity-feed job-history-feed">
          {sections.map((section) => (
            <section key={section.label} className="activity-section" aria-label={section.label}>
              <h3 className="activity-section-label">{section.label}</h3>
              {section.events.map((event) => (
                <HistoryEventItem key={event.id} event={event} />
              ))}
            </section>
          ))}
        </div>
      )}
    </Drawer>
  );
}

function HistoryEventItem({ event }: { event: JobHistoryResponse }) {
  const [isExpanded, setIsExpanded] = useState(false);
  const hasChanges = event.changes.length > 0;
  const actorName = event.actorName?.trim() || 'Workslip';
  const eventLabel = formatEventType(event.eventType);
  const Icon = getEventIcon(event.eventType);
  const tone = getEventTone(event.eventType);
  const summary = event.summary?.trim() || getFallbackSummary(event.eventType);

  const content = (
    <>
      <span className="activity-heading">
        <strong className="activity-title">
          <span className="job-history-actor">{actorName}</span>
          {' · '}
          {eventLabel.toLocaleLowerCase('da-DK')}
        </strong>
        <time
          className="activity-time"
          dateTime={event.createdAt}
          title={new Date(event.createdAt).toLocaleString('da-DK')}
        >
          {formatRelativeActivityTime(event.createdAt)}
        </time>
      </span>
      <span className="activity-body">{summary}</span>
      <span className="activity-meta">
        <span className="activity-badge">
          {Icon}
          {eventLabel}
        </span>
        {hasChanges && (
          <span className="activity-meta-item">
            {event.changes.length} {event.changes.length === 1 ? 'feltændring' : 'feltændringer'}
          </span>
        )}
      </span>
    </>
  );

  return (
    <article className="activity-row job-history-event">
      <span className={`activity-avatar ${tone}`} aria-hidden="true">
        {getActivityInitials(event.actorName)}
      </span>

      <div className="activity-content">
        {hasChanges ? (
          <button
            type="button"
            className="activity-primary-action job-history-event-trigger"
            onClick={() => setIsExpanded((current) => !current)}
            aria-expanded={isExpanded}
            aria-label={`${actorName}: ${eventLabel}. ${event.changes.length} ${event.changes.length === 1 ? 'feltændring' : 'feltændringer'}. ${isExpanded ? 'Skjul detaljer' : 'Vis detaljer'}`}
          >
            {content}
          </button>
        ) : (
          <div className="job-history-event-static">{content}</div>
        )}

        {hasChanges && (
          <div className="activity-actions">
            <button
              type="button"
              className="activity-action job-history-expand"
              onClick={() => setIsExpanded((current) => !current)}
              aria-expanded={isExpanded}
            >
              <ChevronDown className={isExpanded ? 'job-history-chevron-expanded' : ''} size={15} />
              {isExpanded ? 'Skjul ændringer' : 'Vis ændringer'}
            </button>
          </div>
        )}
      </div>

      {isExpanded && hasChanges && (
        <div className="activity-details job-history-details">
          <div className="job-history-change-list">
            {event.changes.map((change, index) => (
              <div key={`${change.propertyName}-${index}`} className="job-history-change-row">
                <strong>{change.displayName || change.propertyName}</strong>
                <HistoryChangeValues
                  eventType={event.eventType}
                  before={change.before}
                  after={change.after}
                />
              </div>
            ))}
          </div>
        </div>
      )}
    </article>
  );
}

function HistoryChangeValues({
  eventType,
  before,
  after,
}: {
  eventType: string;
  before: string | null | undefined;
  after: string | null | undefined;
}) {
  const normalizedType = eventType.toLocaleLowerCase('da-DK');

  if (normalizedType === 'added') {
    return (
      <div className="job-history-change-values job-history-change-values-single">
        <span className="job-history-change-label">Værdi</span>
        <span className="job-history-change-new">{formatValue(after)}</span>
      </div>
    );
  }

  return (
    <div className="job-history-change-values">
      <span>
        <span className="job-history-change-label">Før</span>
        <span className="job-history-change-old">{formatValue(before)}</span>
      </span>
      <span className="job-history-change-arrow" aria-hidden="true">→</span>
      <span>
        <span className="job-history-change-label">Efter</span>
        <span className="job-history-change-new">{formatValue(after)}</span>
      </span>
    </div>
  );
}

function getEventIcon(type: string): ReactNode {
  switch (type.toLocaleLowerCase('da-DK')) {
    case 'added': return <Plus size={13} />;
    case 'modified': return <Pencil size={13} />;
    case 'deleted': return <Trash2 size={13} />;
    default: return <History size={13} />;
  }
}

function getEventTone(type: string) {
  switch (type.toLocaleLowerCase('da-DK')) {
    case 'added': return 'activity-avatar-success';
    case 'deleted': return 'activity-avatar-danger';
    default: return 'activity-avatar-primary';
  }
}

function formatEventType(type: string) {
  switch (type.toLocaleLowerCase('da-DK')) {
    case 'added': return 'Oprettet';
    case 'modified': return 'Ændret';
    case 'deleted': return 'Slettet';
    default: return type;
  }
}

function getFallbackSummary(type: string) {
  switch (type.toLocaleLowerCase('da-DK')) {
    case 'added': return 'Sagen blev oprettet.';
    case 'modified': return 'Sagen blev opdateret.';
    case 'deleted': return 'Data blev slettet fra sagen.';
    default: return 'Der blev registreret aktivitet på sagen.';
  }
}

function formatValue(value: string | null | undefined): ReactNode {
  if (value === undefined || value === null || value === 'null' || value === '') {
    return <span className="job-history-value-empty">(tom)</span>;
  }
  if (value === 'true') return 'Ja';
  if (value === 'false') return 'Nej';
  return value;
}
