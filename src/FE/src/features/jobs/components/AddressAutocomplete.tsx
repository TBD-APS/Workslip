import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Loader2, MapPin, X } from 'lucide-react';
import { useAddressAutocomplete, type AddressSuggestion } from '../hooks/useAddressAutocomplete';

type AddressAutocompleteProps = {
  value: string;
  onTextChange: (text: string) => void;
  onSelectSuggestion: (suggestion: AddressSuggestion) => void;
  onClear?: () => void;
  error?: string;
  required?: boolean;
  placeholder?: string;
  readOnly?: boolean;
};

export function AddressAutocomplete({
  value,
  onTextChange,
  onSelectSuggestion,
  onClear,
  error,
  required,
  placeholder,
  readOnly,
}: AddressAutocompleteProps) {
  const { suggestions, isLoading, search, clear } = useAddressAutocomplete();
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const inputRef = useRef<HTMLInputElement>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const listboxRef = useRef<HTMLUListElement>(null);
  const optionRefs = useRef<Array<HTMLLIElement | null>>([]);
  const blurTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const suppressFocusSearchRef = useRef(false);
  const listboxId = useId();
  const visibleActiveIndex = activeIndex >= 0 && activeIndex < suggestions.length ? activeIndex : -1;
  const showSuggestions = !readOnly && isOpen && suggestions.length > 0;

  const clearBlurTimer = useCallback(() => {
    clearTimeout(blurTimerRef.current);
    blurTimerRef.current = undefined;
  }, []);

  useEffect(() => clearBlurTimer, [clearBlurTimer]);

  useEffect(() => {
    const listbox = listboxRef.current;
    const option = optionRefs.current[visibleActiveIndex];
    if (!listbox || !option) return;

    const optionTop = option.offsetTop;
    const optionBottom = optionTop + option.offsetHeight;
    if (optionTop < listbox.scrollTop) {
      listbox.scrollTop = optionTop;
    } else if (optionBottom > listbox.scrollTop + listbox.clientHeight) {
      listbox.scrollTop = optionBottom - listbox.clientHeight;
    }
  }, [visibleActiveIndex]);

  const focusInputWithoutScrolling = useCallback(() => {
    const input = inputRef.current;
    if (!input) return;

    suppressFocusSearchRef.current = true;
    try {
      input.focus({ preventScroll: true });
    } finally {
      suppressFocusSearchRef.current = false;
    }
  }, []);

  const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const text = e.target.value;
    clearBlurTimer();
    onTextChange(text);
    clear();
    search(text);
    setActiveIndex(-1);
    setIsOpen(true);
  }, [clear, clearBlurTimer, onTextChange, search]);

  const handleSelect = useCallback((suggestion: AddressSuggestion) => {
    if (readOnly) return;

    clearBlurTimer();
    onSelectSuggestion(suggestion);
    clear();
    setActiveIndex(-1);
    setIsOpen(false);
    focusInputWithoutScrolling();
  }, [clear, clearBlurTimer, focusInputWithoutScrolling, onSelectSuggestion, readOnly]);

  const handleClear = useCallback(() => {
    if (readOnly) return;

    clearBlurTimer();
    onClear?.();
    clear();
    setActiveIndex(-1);
    setIsOpen(false);
    focusInputWithoutScrolling();
  }, [clear, clearBlurTimer, focusInputWithoutScrolling, onClear, readOnly]);

  const handleFocus = useCallback(() => {
    clearBlurTimer();
    if (suppressFocusSearchRef.current || readOnly) return;

    if (value) {
      search(value);
      setActiveIndex(-1);
      setIsOpen(true);
    }
  }, [clearBlurTimer, readOnly, search, value]);

  const handleBlur = useCallback((e: React.FocusEvent) => {
    if (wrapperRef.current?.contains(e.relatedTarget)) return;
    clearBlurTimer();
    blurTimerRef.current = setTimeout(() => {
      setActiveIndex(-1);
      setIsOpen(false);
      blurTimerRef.current = undefined;
    }, 150);
  }, [clearBlurTimer]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    if (readOnly || e.nativeEvent.isComposing) return;

    if (e.key === 'ArrowDown' && suggestions.length > 0) {
      e.preventDefault();
      setIsOpen(true);
      setActiveIndex((current) => current < 0 || current >= suggestions.length - 1 ? 0 : current + 1);
      return;
    }

    if (e.key === 'ArrowUp' && suggestions.length > 0) {
      e.preventDefault();
      setIsOpen(true);
      setActiveIndex((current) => current <= 0 || current >= suggestions.length ? suggestions.length - 1 : current - 1);
      return;
    }

    if (e.key === 'Enter' && showSuggestions && visibleActiveIndex >= 0) {
      e.preventDefault();
      handleSelect(suggestions[visibleActiveIndex]);
      return;
    }

    if (e.key === 'Escape' && isOpen) {
      e.preventDefault();
      clear();
      setActiveIndex(-1);
      setIsOpen(false);
      return;
    }

    if (e.key === 'Tab' && isOpen) {
      clear();
      setActiveIndex(-1);
      setIsOpen(false);
    }
  }, [clear, handleSelect, isOpen, readOnly, showSuggestions, suggestions, visibleActiveIndex]);

  return (
    <div className="form-group address-autocomplete" ref={wrapperRef}>
      <div className="address-input-wrapper">
        <input
          ref={inputRef}
          className={`form-input${error ? ' form-input-invalid' : ''}`}
          value={value}
          onChange={handleInputChange}
          onFocus={handleFocus}
          onBlur={handleBlur}
          onKeyDown={handleKeyDown}
          placeholder={placeholder ?? 'Søg adresse...'}
          required={required}
          readOnly={readOnly}
          autoComplete="off"
          role="combobox"
          aria-label={placeholder ?? 'Adresse'}
          aria-autocomplete="list"
          aria-expanded={showSuggestions}
          aria-controls={showSuggestions ? listboxId : undefined}
          aria-activedescendant={visibleActiveIndex >= 0 ? `${listboxId}-option-${visibleActiveIndex}` : undefined}
        />
        {isLoading && <Loader2 size={16} className="address-spinner" />}
        {!readOnly && !isLoading && value && onClear && (
          <button
            type="button"
            className="address-clear-btn"
            title="Fjern adresse"
            aria-label="Fjern adresse"
            onMouseDown={(e) => e.preventDefault()}
            onClick={handleClear}
          >
            <X size={16} />
          </button>
        )}
      </div>
      {showSuggestions && (
        <ul ref={listboxRef} id={listboxId} className="address-suggestions" role="listbox">
          {suggestions.map((s, index) => (
            <li
              key={s.display}
              ref={(element) => { optionRefs.current[index] = element; }}
              id={`${listboxId}-option-${index}`}
              className={`address-suggestion-item${visibleActiveIndex === index ? ' is-active' : ''}`}
              role="option"
              aria-selected={visibleActiveIndex === index}
              onMouseDown={(e) => e.preventDefault()}
              onMouseEnter={() => setActiveIndex(index)}
              onClick={() => handleSelect(s)}
            >
              <MapPin size={14} className="address-suggestion-icon" />
              <span>{s.display}</span>
            </li>
          ))}
        </ul>
      )}
      {error && <p className="form-error-text">{error}</p>}
    </div>
  );
}
