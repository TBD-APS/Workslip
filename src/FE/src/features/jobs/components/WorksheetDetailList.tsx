import { useMemo } from 'react';
import type { WorksheetResponse } from '../../../api/generated/models';
import { formatDateLong } from '../../../lib/formatDate';
import { formatNumber, formatUnit, parseNullableNumber } from '../../../lib/formatUtils';

type WorksheetDetailListProps = {
  worksheets: WorksheetResponse[];
  className?: string;
};

export function WorksheetDetailList({ worksheets, className }: WorksheetDetailListProps) {
  const sorted = useMemo(() => {
    return [...worksheets].sort((a, b) => {
      const nameA = a.userDisplayName || a.userId;
      const nameB = b.userDisplayName || b.userId;
      const byName = nameA.localeCompare(nameB);
      if (byName !== 0) return byName;
      return b.workDate.localeCompare(a.workDate);
    });
  }, [worksheets]);

  if (sorted.length === 0) {
    return <p className="empty-state-text">Ingen timesedler registreret.</p>;
  }

  return (
    <ul className={`worksheet-list worksheet-list--detail${className ? ` ${className}` : ''}`}>
      {sorted.map((worksheet) => {
        const hours = parseNullableNumber(worksheet.hoursWorked);
        const userName = worksheet.userDisplayName || worksheet.userId;
        return (
          <li key={worksheet.id} className="worksheet-list-item worksheet-list-item--detail">
            <div className="worksheet-list-item-main worksheet-list-item-main--detail">
              <span className="worksheet-list-item-title" title={userName}>{userName}</span>
              <span className="worksheet-list-item-subtitle worksheet-list-item-subtitle--detail">{formatDateLong(worksheet.workDate) ?? ''}</span>
            </div>
            <div className="worksheet-list-item-meta">
              {worksheet.sleptOnJob && <span className="worksheet-list-item-tag">Udlæg</span>}
              <div className="worksheet-list-item-badge">
                <strong>{formatNumber(hours)}</strong>
                <span>{formatUnit(hours, 'time', 'timer')}</span>
              </div>
            </div>
          </li>
        );
      })}
    </ul>
  );
}
