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
