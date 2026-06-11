import { History, X, User, Clock, ChevronDown, ChevronUp } from 'lucide-react';
import { useState, useEffect } from 'react';
import { useGetApiJobsIdHistory } from '../../../api/generated/jobs/jobs';
import { format } from 'date-fns';
import { da } from 'date-fns/locale';
import type { JobHistoryResponse, PropertyChange } from '../../../api/generated/models';

type JobHistoryDrawerProps = {
  jobId: string;
  isOpen: boolean;
  onClose: () => void;
};

export function JobHistoryDrawer({ jobId, isOpen, onClose }: JobHistoryDrawerProps) {
  const { data: history, isLoading, refetch } = useGetApiJobsIdHistory(jobId, undefined, {
    query: {
      enabled: isOpen,
    },
  });

  useEffect(() => {
    if (isOpen) {
      refetch();
    }
  }, [isOpen, refetch]);

  return (
    <>
      <div 
        className={`drawer-overlay ${isOpen ? 'open' : ''}`} 
        onClick={onClose} 
      />
      <div className={`drawer history-drawer ${isOpen ? 'open' : ''}`}>
        <div className="drawer-header">
          <div className="drawer-title">
            <History size={20} />
            <h2>Sags historik</h2>
          </div>
          <button className="btn-icon" onClick={onClose}>
            <X size={24} />
          </button>
        </div>

        <div className="drawer-content">
          {isLoading ? (
            <div className="drawer-loading">Henter historik...</div>
          ) : !history || history.length === 0 ? (
            <div className="drawer-empty">Ingen historik fundet for denne sag.</div>
          ) : (
            <div className="history-timeline">
              {history.map((event) => (
                <HistoryEventItem key={event.id} event={event} />
              ))}
            </div>
          )}
        </div>
      </div>
    </>
  );
}

function HistoryEventItem({ event }: { event: JobHistoryResponse }) {
  const [isExpanded, setIsExpanded] = useState(false);
  const hasChanges = event.changes && event.changes.length > 0;

  return (
    <div className="history-event">
      <div className="history-event-header" onClick={() => hasChanges && setIsExpanded(!isExpanded)}>
        <div className="history-event-meta">
          <div className="history-event-user">
            <User size={14} />
            <span>{event.actorName || 'System'}</span>
          </div>
          <div className="history-event-time">
            <Clock size={14} />
            <span>{format(new Date(event.createdAt), 'd. MMM yyyy, HH:mm', { locale: da })}</span>
          </div>
        </div>
        <div className="history-event-type-container">
          <span className={`history-event-type event-${event.eventType}`}>
            {formatEventType(event.eventType)}
          </span>
          {hasChanges && (
            <div className="history-expand-icon">
              {isExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
            </div>
          )}
        </div>
      </div>

      {isExpanded && hasChanges && (
        <div className="history-event-details">
          <table className="history-changes-table">
            <thead>
              <tr>
                <th>Felt</th>
                <th>Før</th>
                <th>Efter</th>
              </tr>
            </thead>
            <tbody>
              {event.changes.map((change, idx) => (
                <tr key={idx}>
                  <td className="change-field">{change.displayName || change.propertyName}</td>
                  <td className="change-value-old">{formatValue(change.before)}</td>
                  <td className="change-value-new">{formatValue(change.after)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function formatEventType(type: string) {
  switch (type.toLowerCase()) {
    case 'added': return 'Oprettet';
    case 'modified': return 'Ændret';
    case 'deleted': return 'Slettet';
    default: return type;
  }
}

function formatValue(value: string | undefined) {
  if (value === undefined || value === null || value === 'null' || value === '') {
    return <span className="value-empty">(tom)</span>;
  }
  if (value === 'true') return 'Ja';
  if (value === 'false') return 'Nej';
  return value;
}
