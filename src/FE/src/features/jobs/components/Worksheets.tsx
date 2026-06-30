import type { WorksheetResponse } from '../../../api/generated/models';
import { formatDateLong } from '../../../lib/formatDate';
import { formatNumber, formatUnit, parseNullableNumber } from '../../../lib/formatUtils';

export function Worksheets({ worksheets }: { worksheets: WorksheetResponse[] }) {
  if (worksheets.length === 0) {
    return <p className="empty-state-text">Ingen timesedler registreret.</p>;
  }

  return (
    <ul className="worksheet-list worksheet-list--detail report-overview-timesheet-list">
      {worksheets.map((worksheet) => {
        const hours = parseNullableNumber(worksheet.hoursWorked);
        const userName = worksheet.userDisplayName || worksheet.userId;
        return (
          <li key={worksheet.id} className="worksheet-list-item worksheet-list-item--detail">
            <div className="worksheet-list-item-main worksheet-list-item-main--detail">
              <span className="worksheet-list-item-title" title={userName}>{userName}</span>
              <span className="worksheet-list-item-subtitle worksheet-list-item-subtitle--detail">{formatDateLong(worksheet.workDate)}</span>
            </div>

            <div className="worksheet-list-item-meta">
              <div className="worksheet-list-item-badge">
                <strong>{formatNumber(hours)}</strong>
                <span>{formatUnit(hours, 'time', 'timer')}</span>
              </div>
              {worksheet.sleptOnJob && <span className="worksheet-list-item-tag">Udlæg</span>}
            </div>
          </li>
        );
      })}
    </ul>
  );
}
