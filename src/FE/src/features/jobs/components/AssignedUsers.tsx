import { User } from 'lucide-react';
import type { JobReportSummaryViewModel } from '../../../api/generated/models';

export function AssignedUsers({ users }: { users: JobReportSummaryViewModel['assignedUsers'] }) {
  if (users.length === 0) {
    return <p className="empty-state-text report-overview-block-gap">Ingen montører tildelt.</p>;
  }

  return (
    <div className="report-overview-chip-list report-overview-block-gap">
      {users.map((user) => (
        <span key={user.id} className="report-overview-chip">
          <User size={12} />
          <span>{user.displayName}</span>
        </span>
      ))}
    </div>
  );
}
