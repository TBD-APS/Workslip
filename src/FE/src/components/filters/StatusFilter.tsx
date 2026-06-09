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
  const toggle = (value: T) => {
    if (selected.includes(value)) {
      onChange(selected.filter((s) => s !== value));
    } else {
      onChange([...selected, value]);
    }
  };

  return (
    <div className="status-filter">
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
  );
}
