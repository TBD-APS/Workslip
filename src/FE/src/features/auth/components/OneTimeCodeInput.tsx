import { forwardRef, useState, type InputHTMLAttributes } from 'react';
import './OneTimeCodeInput.css';

const CODE_LENGTH = 6;

interface OneTimeCodeInputProps extends Omit<
  InputHTMLAttributes<HTMLInputElement>,
  'type' | 'inputMode' | 'maxLength' | 'value' | 'onChange' | 'className'
> {
  value: string;
  onValueChange: (value: string) => void;
  hasError?: boolean;
}

export const OneTimeCodeInput = forwardRef<HTMLInputElement, OneTimeCodeInputProps>(
  function OneTimeCodeInput(
    {
      value,
      onValueChange,
      hasError = false,
      disabled = false,
      onBlur,
      onFocus,
      onPaste,
      ...inputProps
    },
    ref,
  ) {
    const [isFocused, setIsFocused] = useState(false);
    const normalizedValue = value.replace(/\D/g, '').slice(0, CODE_LENGTH);
    const activeIndex = Math.min(normalizedValue.length, CODE_LENGTH - 1);

    return (
      <div
        className={`otp-input${hasError ? ' otp-input-invalid' : ''}${disabled ? ' otp-input-disabled' : ''}`}
      >
        <input
          {...inputProps}
          ref={ref}
          type="text"
          inputMode="numeric"
          pattern="[0-9]*"
          maxLength={CODE_LENGTH}
          autoComplete="one-time-code"
          value={normalizedValue}
          disabled={disabled}
          className="otp-input-native"
          onChange={(event) => {
            onValueChange(event.currentTarget.value.replace(/\D/g, '').slice(0, CODE_LENGTH));
          }}
          onPaste={(event) => {
            onPaste?.(event);
            if (event.defaultPrevented) return;

            const pastedDigits = event.clipboardData
              .getData('text')
              .replace(/\D/g, '')
              .slice(0, CODE_LENGTH);

            if (!pastedDigits) return;

            event.preventDefault();
            onValueChange(pastedDigits);
          }}
          onFocus={(event) => {
            setIsFocused(true);
            onFocus?.(event);
          }}
          onBlur={(event) => {
            setIsFocused(false);
            onBlur?.(event);
          }}
        />

        <div className="otp-input-cells" aria-hidden="true">
          {Array.from({ length: CODE_LENGTH }, (_, index) => {
            const digit = normalizedValue[index] ?? '';
            const isActive = isFocused && index === activeIndex;

            return (
              <span
                key={index}
                className={`otp-input-cell${digit ? ' otp-input-cell-filled' : ''}${isActive ? ' otp-input-cell-active' : ''}`}
              >
                {digit}
              </span>
            );
          })}
        </div>
      </div>
    );
  },
);
