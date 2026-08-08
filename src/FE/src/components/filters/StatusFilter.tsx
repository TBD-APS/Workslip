import { useCallback, useEffect, useRef, useState } from 'react';

const LAST_ACTIVE_KEY = 'statusFilter:lastActive';

export function getSavedStatusFilter<T extends string>(sectionKey: string, defaults: T[]): T[] {
  try {
    const lastActive = sessionStorage.getItem(LAST_ACTIVE_KEY);
    sessionStorage.setItem(LAST_ACTIVE_KEY, sectionKey);

    if (lastActive !== sectionKey) {
      if (lastActive) {
        sessionStorage.removeItem(`statusFilter:${lastActive}`);
      }
      sessionStorage.removeItem(`statusFilter:${sectionKey}`);
      return defaults;
    }

    const saved = sessionStorage.getItem(`statusFilter:${sectionKey}`);
    if (saved) {
      const parsed = JSON.parse(saved);
      if (Array.isArray(parsed) && parsed.length > 0) {
        return [parsed[0] as T];
      }
    }
  } catch {}
  return defaults;
}

export function saveStatusFilter(sectionKey: string, statuses: string[]) {
  sessionStorage.setItem(`statusFilter:${sectionKey}`, JSON.stringify(statuses));
}

/** Call on mount on pages that are section boundaries (e.g. UserList, any page outside the filter's section). */
export function announceSection(sectionKey: string) {
  const lastActive = sessionStorage.getItem(LAST_ACTIVE_KEY);
  sessionStorage.setItem(LAST_ACTIVE_KEY, sectionKey);
  if (lastActive && lastActive !== sectionKey) {
    sessionStorage.removeItem(`statusFilter:${lastActive}`);
  }
}

type StatusOption<T extends string> = {
  value: T;
  label: string;
};

type StatusFilterProps<T extends string> = {
  options: StatusOption<T>[];
  selected: T[];
  onChange: (selected: T[]) => void;
};

export function StatusFilter<T extends string>({
  options,
  selected,
  onChange,
}: StatusFilterProps<T>) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);

  const updateScrollState = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    const { scrollLeft, scrollWidth, clientWidth } = el;
    setCanScrollLeft(scrollLeft > 4);
    setCanScrollRight(scrollLeft + clientWidth < scrollWidth - 4);
  }, []);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;

    updateScrollState();

    el.addEventListener('scroll', updateScrollState, { passive: true });

    const observer = new ResizeObserver(updateScrollState);
    observer.observe(el);

    return () => {
      el.removeEventListener('scroll', updateScrollState);
      observer.disconnect();
    };
  }, [options, updateScrollState]);

  const toggle = (value: T) => {
    if (selected.includes(value)) {
      onChange([]);
    } else {
      onChange([value]);
    }
  };

  return (
    <div
      className="status-filter-scroll"
      data-scroll-left={canScrollLeft}
      data-scroll-right={canScrollRight}
    >
      <div className="status-filter" ref={scrollRef}>
        {options.map((option) => (
          <button
            key={option.value}
            className={`status-filter-btn${selected.includes(option.value) ? ' selected' : ''}`}
            type="button"
            onClick={() => toggle(option.value)}
            aria-pressed={selected.includes(option.value)}
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>
  );
}