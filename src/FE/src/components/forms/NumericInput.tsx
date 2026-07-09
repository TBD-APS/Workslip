import type { ChangeEvent } from 'react';

type NumericKind = 'integer' | 'decimal';

type NumericInputProps = {
  id?: string;
  className?: string;
  value: string | number | null | undefined;
  placeholder?: string;
  kind?: NumericKind;
  min?: number;
  max?: number;
  disabled?: boolean;
  onChange: (value: string) => void;
};

/**
 * Mobile-safe numeric input.
 *
 * Always renders as type="text" with an inputMode hint so iOS Safari and
 * Android Chrome show a numeric keypad that includes the locale's decimal
 * separator ("," on Danish locales). Accepts both "." and "," on input;
 * the onChange callback receives the raw text so callers can normalise
 * with their existing parser (e.g. parseHours replaces "," with ".").
 *
 * - kind="integer"  -> inputMode="numeric",  accepts digits only
 * - kind="decimal"  -> inputMode="decimal",  accepts digits + "." + ","
 */
export function NumericInput({
  id,
  className,
  value,
  placeholder,
  kind = 'decimal',
  min,
  max,
  disabled,
  onChange,
}: NumericInputProps) {
  const isDecimal = kind === 'decimal';
  const pattern = isDecimal ? '[0-9]*([.,][0-9]+)?' : '[0-9]*';
  const strip = isDecimal ? /[^0-9.,]/g : /[^0-9]/g;

  const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
    onChange(event.target.value.replace(strip, ''));
  };

  return (
    <input
      id={id}
      className={className ?? 'form-input'}
      type="text"
      inputMode={isDecimal ? 'decimal' : 'numeric'}
      pattern={pattern}
      min={min}
      max={max}
      value={value ?? ''}
      onChange={handleChange}
      placeholder={placeholder}
      disabled={disabled}
    />
  );
}
