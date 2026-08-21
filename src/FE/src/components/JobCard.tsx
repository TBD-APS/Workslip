import { ChevronRight, Clock, MapPin, Timer, User } from 'lucide-react';
import { CopyAddressButton } from './CopyAddressButton';
import { formatDate } from '../lib/formatDate';
import { formatJobStatus, formatJobType } from '../lib/statusLabels';

type AssignedUserLike = {
  id: string;
  displayName: string;
};

type JobCardProps = {
  id: string;
  reportNumber?: string | null;
  status: string;
  customerName?: string | null;
  taskDescription?: string | null;
  jobType?: string | null;
  address?: string | null;
  installationTypes?: string[] | null;
  totalHours?: number | string | null;
  assignedUsers?: AssignedUserLike[] | null;
  updatedAt?: string | null;
  isSeen?: boolean;
  isNewRejection?: boolean;
  showUnassigned?: boolean;
  onOpen: () => void;
};

export function JobCard({
  id,
  reportNumber,
  status,
  customerName,
  taskDescription,
  jobType,
  address,
  installationTypes,
  totalHours,
  assignedUsers,
  updatedAt,
  isSeen = true,
  isNewRejection = false,
  showUnassigned = false,
  onOpen,
}: JobCardProps) {
  const hasAssignmentData = assignedUsers != null;
  const users = assignedUsers ?? [];
  const title = customerName || taskDescription || 'Sag uden kunde';
  const number = (reportNumber || id.slice(0, 4)).toUpperCase();
  const isRejected = status === 'Rejected';

  return (
    <div
      className={`job-card${isRejected ? ' job-card--rejected' : ''}`}
      onClick={onOpen}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return;
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onOpen();
        }
      }}
      role="link"
      tabIndex={0}
    >
      <div className="job-card-top">
        <div>
          <span className="job-number">
            SAG-{number}
            {jobType && <><span className="job-number-sep">&middot;</span>{formatJobType(jobType)}</>}
            <span className="job-number-sep">&middot;</span>
            <span className="job-number-status">{formatJobStatus(status)}</span>
          </span>
          {status === 'InReview' && <span className="review-dot" aria-hidden="true" />}
          {status === 'Approved' && <span className="approved-dot" aria-hidden="true" />}
          {!isSeen && <span className="unread-dot" role="img" aria-label="Ulæst" />}
          {isNewRejection && <span className="rejected-dot" role="img" aria-label="Ny afvisning" />}
          {showUnassigned && hasAssignmentData && users.length === 0 && <span className="unassigned-dot" role="img" aria-label="Ikke tildelt" />}
          <h3 className="job-customer">{title}</h3>
        </div>
      </div>

      <p className="job-address-row">
        <MapPin size={14} aria-hidden="true" />
        <span className="job-address">{address || 'Ingen adresse angivet'}</span>
        <CopyAddressButton address={address} />
      </p>

      <div className="job-card-meta">
        {installationTypes && installationTypes.length > 0 && (
          <span className="meta-item">
            <span className="cell-comma-list">
              {installationTypes.map((type) => (
                <span key={type} className="cell-comma-list-item">{type}</span>
              ))}
            </span>
          </span>
        )}
        {totalHours != null && (
          <span className="meta-item meta-hours">
            <Timer size={14} aria-hidden="true" /> {totalHours}
          </span>
        )}
        <span className="meta-item meta-updated">
          <Clock size={14} aria-hidden="true" /> Opdateret {formatDate(updatedAt) ?? '–'}
        </span>
      </div>

      <div className="job-card-footer">
        {hasAssignmentData ? (
          users.length > 0 ? (
            <span className="cell-comma-list">
              {users.map((user) => (
                <span key={user.id} className="cell-comma-list-item">{user.displayName}</span>
              ))}
            </span>
          ) : (
            <span className="unassigned">
              <User size={12} aria-hidden="true" />
              <span>Ikke tildelt</span>
            </span>
          )
        ) : (
          <span aria-hidden="true" />
        )}
        <span className="btn-icon" aria-hidden="true">
          <ChevronRight size={20} />
        </span>
      </div>
    </div>
  );
}
