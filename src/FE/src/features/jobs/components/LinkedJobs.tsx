import type { JobLinkInfoResponse } from '../../../api/generated/models';

export function LinkedJobs({ links, onOpen }: { links: JobLinkInfoResponse[]; onOpen: (linkedJobId: string) => void }) {
  if (links.length === 0) {
    return <p className="empty-state-text">Ingen tilknyttede sager.</p>;
  }

  return (
    <div className="report-overview-link-list">
      {links.map((link) => (
        <button
          key={link.id}
          type="button"
          className="linked-job-row"
          onClick={() => onOpen(link.linkedReportId)}
        >
          <span className="linked-job-link">SAG-{link.linkedReportNumber}</span>
          <span className="linked-job-customer">{link.linkedCustomerName || 'Ukendt kunde'}</span>
        </button>
      ))}
    </div>
  );
}
