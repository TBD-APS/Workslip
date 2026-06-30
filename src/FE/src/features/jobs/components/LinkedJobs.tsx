import { ChevronRight } from 'lucide-react';
import type { JobLinkInfoResponse } from '../../../api/generated/models';

export function LinkedJobs({ links, onOpen }: { links: JobLinkInfoResponse[]; onOpen: (linkedJobId: string) => void }) {
  if (links.length === 0) {
    return <p className="empty-state-text">Ingen tilknyttede sager.</p>;
  }

  return (
    <div className="report-overview-link-list">
      {links.map((link) => (
        <button key={link.id} type="button" className="report-overview-link-card" onClick={() => onOpen(link.linkedReportId)}>
          <div className="report-overview-top-row">
            <span className="job-number">SAG-{link.linkedReportNumber}</span>
            <span className="report-overview-customer">{link.linkedCustomerName || 'Ukendt kunde'}</span>
          </div>
          <div className="report-overview-link-card-footer">
            <span className="report-overview-address">{link.linkedAddress || 'Ukendt adresse'}</span>
            <span className="btn-icon" aria-label="Åbn tilknyttet sag">
              <ChevronRight size={20} />
            </span>
          </div>
        </button>
      ))}
    </div>
  );
}
