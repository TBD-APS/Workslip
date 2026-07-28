import { useEffect, useRef, useState } from 'react';
import { ChevronRight } from 'lucide-react';

type CollapsibleSectionProps = {
  icon: React.ReactNode;
  title: string;
  children: React.ReactNode;
  className?: string;
  defaultOpen?: boolean;
  open?: boolean;
  onToggle?: (open: boolean) => void;
  scrollOnOpen?: boolean;
};

type CollapsibleHistoryState = {
  __workslipCollapsibleSections?: Record<string, boolean>;
};

function getPersistedOpenState(key: string, fallback: boolean): boolean {
  if (typeof window === 'undefined') return fallback;
  const historyState = window.history.state as CollapsibleHistoryState | null;
  return historyState?.__workslipCollapsibleSections?.[key] ?? fallback;
}

function persistOpenState(key: string, open: boolean) {
  if (typeof window === 'undefined') return;

  const historyState = (window.history.state ?? {}) as CollapsibleHistoryState & Record<string, unknown>;
  window.history.replaceState(
    {
      ...historyState,
      __workslipCollapsibleSections: {
        ...historyState.__workslipCollapsibleSections,
        [key]: open,
      },
    },
    '',
  );
}

export function CollapsibleSection({ icon, title, children, className, defaultOpen = true, open, onToggle, scrollOnOpen = true }: CollapsibleSectionProps) {
  const persistenceKey = className ?? title;
  const [internalOpen, setInternalOpen] = useState(() => getPersistedOpenState(persistenceKey, defaultOpen));
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
    if (isOpen && scrollOnOpen) {
      contentRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  }, [isOpen, isControlled, scrollOnOpen]);

  const handleToggle = () => {
    if (isControlled) {
      onToggle?.(!open);
    } else {
      setInternalOpen((previousOpen) => {
        const nextOpen = !previousOpen;
        persistOpenState(persistenceKey, nextOpen);
        return nextOpen;
      });
    }
  };

  return (
    <section className={`detail-section collapsible-section${className ? ` ${className}` : ''}`}>
      <button
        className="collapsible-section-trigger"
        type="button"
        onClick={handleToggle}
        aria-expanded={isOpen}
      >
        <span className="section-title-row">
          {icon}
          <h3>{title}</h3>
          <ChevronRight className={isOpen ? 'collapsible-chevron open' : 'collapsible-chevron'} size={18} />
        </span>
      </button>

      {isOpen && <div ref={contentRef} className="collapsible-section-content">{children}</div>}
    </section>
  );
}
