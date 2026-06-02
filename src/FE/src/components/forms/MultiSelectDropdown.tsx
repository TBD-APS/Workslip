import { useEffect, useRef, useState } from 'react';
import { CheckCircle2, ChevronRight } from 'lucide-react';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export type MultiSelectOption = {
  id: string;
  label: string;
  description?: string;
};

type MultiSelectDropdownProps = {
  label: string;
  placeholder: string;
  emptyText: string;
  loadingText: string;
  options: MultiSelectOption[];
  selectedIds: string[];
  isLoading?: boolean;
  saveStatus?: SaveStatus;
  icon?: React.ReactNode;
  onChange: (selectedIds: string[]) => void;
};

export function MultiSelectDropdown({
  label,
  placeholder,
  emptyText,
  loadingText,
  options,
  selectedIds,
  isLoading = false,
  saveStatus,
  icon,
  onChange,
}: MultiSelectDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const dropdownRef = useRef<HTMLDivElement | null>(null);
  const selectedOptions = options.filter((option) => selectedIds.includes(option.id));
  const filteredOptions = searchQuery
    ? options.filter((option) => {
        const q = searchQuery.toLowerCase();
        return option.label.toLowerCase().includes(q)
          || (option.description && option.description.toLowerCase().includes(q));
      })
    : options;

  useEffect(() => {
    if (!isOpen) {
      setSearchQuery('');
      return;
    }

    const handlePointerDown = (event: PointerEvent) => {
      if (!dropdownRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [isOpen]);

  const toggleOption = (optionId: string) => {
    if (selectedIds.includes(optionId)) {
      onChange(selectedIds.filter((id) => id !== optionId));
      return;
    }

    onChange([...selectedIds, optionId]);
  };

  return (
    <div className="multi-select-field">
      <div className="multi-select-field-header">
        <label className="form-label">{label}</label>
        {saveStatus && <StatusIndicator saveStatus={saveStatus} />}
      </div>

      <div className="multi-select-dropdown" ref={dropdownRef}>
        <button
          className="multi-select-trigger"
          type="button"
          disabled={isLoading}
          onClick={() => setIsOpen((open) => !open)}
          aria-expanded={isOpen}
        >
          <span className="multi-select-trigger-content">
            {icon}
            {selectedOptions.length > 0 ? `${selectedOptions.length} valgt` : placeholder}
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
                onChange={(e) => setSearchQuery(e.target.value)}
                autoFocus
              />
            </div>
            {isLoading && <p className="multi-select-menu-empty">{loadingText}</p>}
            {!isLoading && filteredOptions.length === 0 && <p className="multi-select-menu-empty">{searchQuery ? 'Ingen resultater' : emptyText}</p>}
            {filteredOptions.map((option) => {
              const isSelected = selectedIds.includes(option.id);
              return (
                <button
                  key={option.id}
                  className={isSelected ? 'multi-select-option selected' : 'multi-select-option'}
                  type="button"
                  onClick={() => toggleOption(option.id)}
                >
                  <span className="multi-select-checkbox" aria-hidden="true">
                    {isSelected && <CheckCircle2 size={14} />}
                  </span>
                  <span className="multi-select-option-text">
                    <span>{option.label}</span>
                    {option.description && <small>{option.description}</small>}
                  </span>
                </button>
              );
            })}
          </div>
        )}
      </div>

      <div className="multi-select-chips">
        {selectedOptions.length > 0 ? (
          selectedOptions.map((option) => (
            <button
              key={option.id}
              className="multi-select-chip"
              type="button"
              onClick={() => toggleOption(option.id)}
              aria-label={`Fjern ${option.label}`}
            >
              <span>{option.label}</span>
              <span className="multi-select-chip-remove" aria-hidden="true">x</span>
            </button>
          ))
        ) : (
          <span className="multi-select-empty">Ingen valgt</span>
        )}
      </div>
    </div>
  );
}

function StatusIndicator({ saveStatus }: { saveStatus: SaveStatus }) {
  if (saveStatus === 'idle') return null;

  return <span className={`save-indicator ${saveStatus}`}>{saveStatus === 'saving' ? 'Gemmer...' : saveStatus === 'saved' ? 'Gemt' : 'Fejl ved gem'}</span>;
}
