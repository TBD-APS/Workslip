import { CopyAddressButton } from '../../../components/CopyAddressButton';
import type { DetailPair } from '../../../lib/formatUtils';

const COPYABLE_ADDRESS_LABELS = new Set(['Adresse', 'Destination']);

export function DetailGrid({ items }: { items: DetailPair[] }) {
  if (items.length === 0) {
    return <p className="empty-state-text">Ingen oplysninger registreret.</p>;
  }

  return (
    <dl className="attestation-data-list report-overview-data-list">
      {items.map((item) => (
        <div key={item.label} className="attestation-data-pair">
          <dt>{item.label}</dt>
          <dd>
            <span>{item.value}</span>
            {COPYABLE_ADDRESS_LABELS.has(item.label) && <CopyAddressButton address={item.value} />}
          </dd>
        </div>
      ))}
    </dl>
  );
}
