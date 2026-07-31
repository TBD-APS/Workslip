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
        return parsed as T[];
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
  value: T | readonly T[];
  label: string;
};

type StatusFilterProps<T extends string> = {
  options: StatusOption<T>[];
  selected: T[];
  onChange: (selected: T[]) => void;
};

function getOptionValues<T extends string>(option: StatusOption<T>): readonly T[] {
  return Array.isArray(option.value) ? option.value : [option.value as T];
}

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

  const toggle = (option: StatusOption<T>) => {
    const values = getOptionValues(option);
    const allSelected = values.every((value) => selected.includes(value));

    if (allSelected) {
      onChange(selected.filter((status) => !values.includes(status)));
      return;
    }

    onChange([
      ...selected,
      ...values.filter((value) => !selected.includes(value)),
    ]);
  };

  return (
    <div
      className="status-filter-scroll"
      data-scroll-left={canScrollLeft}
      data-scroll-right={canScrollRight}
    >
      <div className="status-filter" ref={scrollRef}>
        {options.map((option) => {
          const values = getOptionValues(option);
          const isSelected = values.every((value) => selected.includes(value));

          return (
            <button
              key={values.join('|')}
              className={`status-filter-btn${isSelected ? ' selected' : ''}`}
              type="button"
              onClick={() => toggle(option)}
              aria-pressed={isSelected}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}