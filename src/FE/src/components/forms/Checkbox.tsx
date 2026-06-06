type CheckboxProps = {
  checked: boolean;
  disabled?: boolean;
  onChange: () => void;
  label: string;
  description?: string;
  alignRight?: boolean;
};

export function Checkbox({ checked, disabled, onChange, label, description, alignRight }: CheckboxProps) {
  return (
    <button
      className={`multi-select-option selection-row ${checked ? 'selected' : ''}${alignRight ? ' selection-align-right' : ''}`}
      type="button"
      disabled={disabled}
      onClick={onChange}
      role="checkbox"
      aria-checked={checked}
    >
      <span className="multi-select-option-text">
        <span>{label}</span>
        {description && <small>{description}</small>}
      </span>
      {checked && <span className="selection-pill" aria-hidden="true">Valgt</span>}
    </button>
  );
}
