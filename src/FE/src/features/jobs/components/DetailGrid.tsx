import type { DetailPair } from '../../../lib/formatUtils';

export function DetailGrid({ items }: { items: DetailPair[] }) {
  if (items.length === 0) {
    return <p className="empty-state-text">Ingen oplysninger registreret.</p>;
  }

  return (
    <dl className="attestation-data-list report-overview-data-list">
      {items.map((item) => (
        <div key={item.label} className="attestation-data-pair">
          <dt>{item.label}</dt>
          <dd>{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}
