import { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { useDropdownContext } from '../../providers/DropdownContext';

export type SingleSelectOption = {
  id: string;
  label: string;
  description?: string;
};

type SingleSelectDropdownProps = {
  label: string;
  placeholder: string;
  emptyText: string;
  loadingText: string;
  options: SingleSelectOption[];
  selectedId: string | null;
  isLoading?: boolean;
  icon?: React.ReactNode;
  footer?: React.ReactNode;
  onSelect: (option: SingleSelectOption) => void;
  onSearchChange?: (query: string) => void;
};

export function SingleSelectDropdown({
  label,
  placeholder,
  emptyText,
  loadingText,
  options,
  selectedId,
  isLoading = false,
  icon,
  footer,
  onSelect,
  onSearchChange,
}: SingleSelectDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const dropdownRef = useRef<HTMLDivElement | null>(null);
  const selectedOption = options.find((option) => option.id === selectedId);
  const { registerOpen, registerClose } = useDropdownContext();
  const filteredOptions = onSearchChange
    ? options
    : searchQuery
      ? options.filter((option) => {
          const q = searchQuery.toLowerCase();
          return option.label.toLowerCase().includes(q)
            || (option.description && option.description.toLowerCase().includes(q));
        })
      : options;

  useEffect(() => {
    if (!isOpen) return undefined;

    const handlePointerDown = (event: PointerEvent) => {
      if (!dropdownRef.current?.contains(event.target as Node)) {
        setSearchQuery('');
        (document.activeElement as HTMLElement)?.blur();
        setIsOpen(false);
        registerClose();
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [isOpen, registerClose]);

  const toggleDropdown = () => {
    if (isOpen) {
      setSearchQuery('');
      setIsOpen(false);
      registerClose();
      return;
    }
    setIsOpen(true);
    registerOpen();
  };

  const handleSelect = useCallback(
    (option: SingleSelectOption) => {
      onSelect(option);
      setSearchQuery('');
      setIsOpen(false);
      registerClose();
    },
    [onSelect, registerClose]
  );

  return (
    <div className="multi-select-field">
      <div className="multi-select-field-header">
        <label className="form-label">{label}</label>
      </div>

      <div className="multi-select-dropdown" ref={dropdownRef}>
        <button
          className="multi-select-trigger"
          type="button"
          disabled={isLoading}
          onClick={toggleDropdown}
          aria-expanded={isOpen}
        >
          <span className="multi-select-trigger-content">
            {icon}
            {selectedOption ? selectedOption.label : placeholder}
          </span>
          <ChevronRight className={isOpen ? 'multi-select-chevron open' : 'multi-select-chevron'} size={16} />
        </button>

        {isOpen && (
          <div className="multi-select-menu">
            <div className="multi-select-search">
              <input
                className="multi-select-search-input"
                type="text"
                placeholder="Søg..."
                value={searchQuery}
                onChange={(e) => {
                  setSearchQuery(e.target.value);
                  onSearchChange?.(e.target.value);
                }}
                autoFocus
              />
            </div>
            {isLoading && <p className="multi-select-menu-empty">{loadingText}</p>}
            {!isLoading && filteredOptions.length === 0 && (
              <p className="multi-select-menu-empty">{searchQuery ? 'Ingen resultater' : emptyText}</p>
            )}
            {filteredOptions.map((option) => {
              const isSelected = option.id === selectedId;
              return (
                <button
                  key={option.id}
                  className={isSelected ? 'multi-select-option selection-row selected' : 'multi-select-option selection-row'}
                  type="button"
                  onClick={() => handleSelect(option)}
                  role="option"
                  aria-selected={isSelected}
                >
                  <span className="multi-select-option-text">
                    <span>{option.label}</span>
                    {option.description && <small>{option.description}</small>}
                  </span>
                </button>
              );
            })}
            {footer && <div className="single-select-footer">{footer}</div>}
          </div>
        )}
      </div>
    </div>
  );
}
