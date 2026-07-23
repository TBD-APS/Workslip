import { History, User, Clock, ChevronDown, ChevronUp, Plus, Pencil, Trash2 } from 'lucide-react';
import { useState, useEffect } from 'react';
import { useGetApiJobsIdHistory } from '../../../api/generated/jobs/jobs';
import type { JobHistoryResponse } from '../../../api/generated/models';
import { formatDateLong } from '../../../lib/formatDate';
import { Drawer } from '../../../components/common/Drawer';

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
    <Drawer isOpen={isOpen} onClose={onClose} title="Sags historik" ariaLabel="Sags historik" icon={<History size={20} />} className="history-drawer">
        <div>
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
    </Drawer>
  );
}

function HistoryEventItem({ event }: { event: JobHistoryResponse }) {
  const [isExpanded, setIsExpanded] = useState(false);
  const hasChanges = event.changes && event.changes.length > 0;
  const overview = event.summary;

  const isAdded = event.eventType.toLowerCase() === 'added';
  const Icon = getEventIcon(event.eventType);

  return (
    <div className="history-event">
      <div className="history-event-header" onClick={() => hasChanges && setIsExpanded(!isExpanded)}>
        <div className="history-event-meta">
          <div className="history-event-user">
            <User size={14} />
            <span>{event.actorName ? `Bruger: ${event.actorName}` : 'System'}</span>
          </div>
          <div className="history-event-time">
            <Clock size={14} />
            <span>{formatDateLong(event.createdAt)}</span>
          </div>
        </div>
        <div className="history-event-type-container">
          <div className="history-event-type-label">
            {Icon}
            <span>{formatEventType(event.eventType)}</span>
          </div>
          {hasChanges && (
            <div className="history-expand-icon">
              {isExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
            </div>
          )}
        </div>
      </div>
      
      {overview && (
        <div className="history-event-summary">
          {overview}
        </div>
      )}

      {isExpanded && hasChanges && (
        <div className="history-event-details">
          <table className="history-changes-table">
            <thead>
              <tr>
                <th style={{ width: '40%' }}>Felt</th>
                {!isAdded && <th style={{ width: '30%' }}>Før</th>}
                <th style={{ width: isAdded ? '60%' : '30%' }}>{isAdded ? 'Værdi' : 'Efter'}</th>
              </tr>
            </thead>
            <tbody>
              {event.changes.map((change, idx) => {
                const isModified = event.eventType.toLowerCase() === 'modified';
                return (
                  <tr key={idx}>
                    <td className="change-field">{change.displayName || change.propertyName}</td>
                    {!isAdded && <td className={isModified ? "change-value-old" : ""}>{formatValue(change.before)}</td>}
                    <td className="change-value-new">{formatValue(change.after)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function getEventIcon(type: string) {
  switch (type.toLowerCase()) {
    case 'added': return <Plus size={14} />;
    case 'modified': return <Pencil size={14} />;
    case 'deleted': return <Trash2 size={14} />;
    default: return null;
  }
}

function formatEventType(type: string) {
  switch (type.toLowerCase()) {
    case 'added': return 'Oprettet';
    case 'modified': return 'Ændret';
    case 'deleted': return 'Slettet';
    default: return type;
  }
}

function formatValue(value: string | null | undefined) {
  if (value === undefined || value === null || value === 'null' || value === '') {
    return <span className="value-empty">(tom)</span>;
  }
  if (value === 'true') return 'Ja';
  if (value === 'false') return 'Nej';
  return value;
}
