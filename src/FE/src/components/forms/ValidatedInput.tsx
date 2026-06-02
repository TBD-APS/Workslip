import type { ValidationResult } from './validators';

type ValidatedInputProps = {
  label: string;
  value: string | null;
  placeholder: string;
  type?: string;
  inputMode?: 'text' | 'numeric' | 'tel' | 'email' | 'url' | 'decimal';
  validate?: (value: string | null) => ValidationResult;
  onChange: (value: string | null) => void;
};

export function ValidatedInput({ label, value, placeholder, type = 'text', inputMode, validate, onChange }: ValidatedInputProps) {
  const error = validate?.(value) ?? null;

  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <input
        className={error ? 'form-input form-input-invalid' : 'form-input'}
        type={type}
        inputMode={inputMode}
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value || null)}
        placeholder={placeholder}
        aria-invalid={Boolean(error)}
      />
      {error && <p className="form-error-text">{error}</p>}
    </div>
  );
}
