import { useState } from 'react';
import { ChevronRight } from 'lucide-react';

type CollapsibleSectionProps = {
  icon: React.ReactNode;
  title: string;
  children: React.ReactNode;
  defaultOpen?: boolean;
};

export function CollapsibleSection({ icon, title, children, defaultOpen = true }: CollapsibleSectionProps) {
  const [isOpen, setIsOpen] = useState(defaultOpen);

  return (
    <section className="detail-section collapsible-section">
      <button
        className="collapsible-section-trigger"
        type="button"
        onClick={() => setIsOpen((open) => !open)}
        aria-expanded={isOpen}
      >
        <span className="section-title-row">
          {icon}
          <h3>{title}</h3>
        </span>
        <ChevronRight className={isOpen ? 'collapsible-chevron open' : 'collapsible-chevron'} size={18} />
      </button>

      {isOpen && <div className="collapsible-section-content">{children}</div>}
    </section>
  );
}
