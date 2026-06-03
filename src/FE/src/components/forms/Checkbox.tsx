import { CheckCircle2 } from 'lucide-react';

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
      className={`multi-select-option ${checked ? 'selected' : ''}${alignRight ? ' checkbox-right' : ''}`}
      type="button"
      disabled={disabled}
      onClick={onChange}
    >
      <span className="multi-select-checkbox" aria-hidden="true">
        {checked && <CheckCircle2 size={14} />}
      </span>
      <span className="multi-select-option-text">
        <span>{label}</span>
        {description && <small>{description}</small>}
      </span>
    </button>
  );
}
