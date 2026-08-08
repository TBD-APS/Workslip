import { useCallback, useRef, useState } from 'react';
import { Loader2, MapPin, X } from 'lucide-react';
import { CopyAddressButton } from '../../../components/CopyAddressButton';
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
    if (readOnly) return;
    const text = e.target.value;
    onTextChange(text);
    search(text);
    setIsOpen(true);
  }, [onTextChange, readOnly, search]);

  const handleSelect = useCallback((suggestion: AddressSuggestion) => {
    if (readOnly) return;
    suppressNextOpen.current = true;
    onSelectSuggestion(suggestion);
    clear();
    setIsOpen(false);
  }, [onSelectSuggestion, clear, readOnly]);

  const handleFocus = useCallback(() => {
    if (readOnly) return;
    if (suppressNextOpen.current) {
      suppressNextOpen.current = false;
      return;
    }
    if (value) {
      search(value);
      setIsOpen(true);
    }
  }, [readOnly, value, search]);

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
        {!readOnly && isLoading && <Loader2 size={16} className="address-spinner" />}
        {!readOnly && !isLoading && value && onClear && (
          <button type="button" className="address-clear-btn" title="Fjern adresse" onClick={onClear}>
            <X size={16} />
          </button>
        )}
        {readOnly && value && <CopyAddressButton address={value} />}
      </div>
      {!readOnly && isOpen && suggestions.length > 0 && (
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
