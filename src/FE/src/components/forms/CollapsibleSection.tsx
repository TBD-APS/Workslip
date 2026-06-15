import { useEffect, useRef, useState } from 'react';
import { ChevronRight } from 'lucide-react';

type CollapsibleSectionProps = {
  icon: React.ReactNode;
  title: string;
  children: React.ReactNode;
  defaultOpen?: boolean;
  open?: boolean;
  onToggle?: (open: boolean) => void;
};

export function CollapsibleSection({ icon, title, children, defaultOpen = true, open, onToggle }: CollapsibleSectionProps) {
  const [internalOpen, setInternalOpen] = useState(defaultOpen);
  const isOpen = open ?? internalOpen;
  const isControlled = open !== undefined;
  const contentRef = useRef<HTMLDivElement | null>(null);
  const isInitialMount = useRef(true);

  useEffect(() => {
    if (isControlled) return;
    if (isInitialMount.current) {
      isInitialMount.current = false;
      return;
    }
    if (isOpen) {
      contentRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  }, [isOpen, isControlled]);

  const handleToggle = () => {
    if (isControlled) {
      onToggle?.(!open);
    } else {
      setInternalOpen((prev) => !prev);
    }
  };

  return (
    <section className="detail-section collapsible-section">
      <button
        className="collapsible-section-trigger"
        type="button"
        onClick={handleToggle}
        aria-expanded={isOpen}
      >
        <span className="section-title-row">
          {icon}
          <h3>{title}</h3>
        </span>
        <ChevronRight className={isOpen ? 'collapsible-chevron open' : 'collapsible-chevron'} size={18} />
      </button>

      {isOpen && <div ref={contentRef} className="collapsible-section-content">{children}</div>}
    </section>
  );
}
