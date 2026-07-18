import { useCallback, useRef, useState } from 'react';
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
  const inputRef = useRef<HTMLInputElement>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const suppressNextOpen = useRef(false);

  const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const text = e.target.value;
    onTextChange(text);
    search(text);
    setIsOpen(true);
  }, [onTextChange, search]);

  const handleSelect = useCallback((suggestion: AddressSuggestion) => {
    suppressNextOpen.current = true;
    onSelectSuggestion(suggestion);
    clear();
    setIsOpen(false);
    inputRef.current?.blur();
  }, [onSelectSuggestion, clear]);

  const handleFocus = useCallback(() => {
    if (suppressNextOpen.current) {
      suppressNextOpen.current = false;
      return;
    }
    if (value) {
      search(value);
      setIsOpen(true);
    }
  }, [value, search]);

  const handleBlur = useCallback((e: React.FocusEvent) => {
    if (wrapperRef.current?.contains(e.relatedTarget)) return;
    setTimeout(() => setIsOpen(false), 150);
  }, []);

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
          placeholder={placeholder ?? 'Søg adresse...'}
          required={required}
          readOnly={readOnly}
          autoComplete="off"
        />
        {isLoading && <Loader2 size={16} className="address-spinner" />}
        {!isLoading && value && onClear && (
          <button type="button" className="address-clear-btn" title="Fjern adresse" onClick={onClear}>
            <X size={16} />
          </button>
        )}
      </div>
      {isOpen && suggestions.length > 0 && (
        <ul className="address-suggestions" role="listbox">
          {suggestions.map((s) => (
            <li
              key={s.display}
              className="address-suggestion-item"
              role="option"
              tabIndex={0}
              onMouseDown={(e) => { e.preventDefault(); handleSelect(s); }}
              onKeyDown={(e) => { if (e.key === 'Enter') handleSelect(s); }}
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
