import { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { useDropdownContext } from '../../providers/DropdownContext';

const COMMIT_DELAY_MS = 1000;

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
  icon?: React.ReactNode;
  commitOnClose?: boolean;
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
  icon,
  commitOnClose = false,
  onChange,
}: MultiSelectDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [draftSelectedIds, setDraftSelectedIds] = useState<string[] | null>(null);
  const dropdownRef = useRef<HTMLDivElement | null>(null);
  const commitTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const { registerOpen, registerClose } = useDropdownContext();
  const activeSelectedIds = commitOnClose && (isOpen || draftSelectedIds) ? draftSelectedIds ?? selectedIds : selectedIds;
  const selectedOptions = options.filter((option) => activeSelectedIds.includes(option.id));
  const filteredOptions = searchQuery
    ? options.filter((option) => {
        const q = searchQuery.toLowerCase();
        return option.label.toLowerCase().includes(q)
          || (option.description && option.description.toLowerCase().includes(q));
      })
    : options;

  const scheduleCommit = useCallback((nextSelectedIds: string[]) => {
    clearTimeout(commitTimerRef.current);
    commitTimerRef.current = setTimeout(() => {
      onChange(nextSelectedIds);
      setDraftSelectedIds(null);
      commitTimerRef.current = undefined;
    }, COMMIT_DELAY_MS);
  }, [onChange]);

  const commitDraftSelection = useCallback(() => {
    if (!commitOnClose || !draftSelectedIds) return;
    if (sameSelection(selectedIds, draftSelectedIds)) {
      setDraftSelectedIds(null);
      return;
    }
    scheduleCommit(draftSelectedIds);
  }, [commitOnClose, draftSelectedIds, scheduleCommit, selectedIds]);
  const commitDraftSelectionRef = useRef(commitDraftSelection);

  useEffect(() => {
    commitDraftSelectionRef.current = commitDraftSelection;
  }, [commitDraftSelection]);

  useEffect(() => {
    if (!isOpen) return undefined;

    const handlePointerDown = (event: PointerEvent) => {
      if (!dropdownRef.current?.contains(event.target as Node)) {
        commitDraftSelectionRef.current();
        setSearchQuery('');
        (document.activeElement as HTMLElement)?.blur();
        setIsOpen(false);
        registerClose();
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [isOpen, registerClose]);

  useEffect(() => () => clearTimeout(commitTimerRef.current), []);

  const toggleDropdown = () => {
    if (!commitOnClose) {
      if (isOpen) {
        setSearchQuery('');
        setIsOpen(false);
        registerClose();
      } else {
        setIsOpen(true);
        registerOpen();
      }
      return;
    }

    if (isOpen) {
      commitDraftSelection();
      setSearchQuery('');
      setIsOpen(false);
      registerClose();
      return;
    }

    clearTimeout(commitTimerRef.current);
    commitTimerRef.current = undefined;
    setDraftSelectedIds(draftSelectedIds ?? selectedIds);
    setIsOpen(true);
    registerOpen();
  };

  const toggleOption = (optionId: string) => {
    const currentIds = commitOnClose ? activeSelectedIds : selectedIds;
    const nextIds = currentIds.includes(optionId)
      ? currentIds.filter((id) => id !== optionId)
      : [...currentIds, optionId];

    if (commitOnClose) {
      setDraftSelectedIds(nextIds);
      if (!isOpen) {
        if (sameSelection(selectedIds, nextIds)) {
          clearTimeout(commitTimerRef.current);
          commitTimerRef.current = undefined;
          setDraftSelectedIds(null);
        } else {
          scheduleCommit(nextIds);
        }
      }
      return;
    }

    onChange(nextIds);
  };

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
              const isSelected = activeSelectedIds.includes(option.id);
              return (
                <button
                  key={option.id}
                  className={isSelected ? 'multi-select-option selection-row selected' : 'multi-select-option selection-row'}
                  type="button"
                  onClick={() => toggleOption(option.id)}
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
            </button>
          ))
        ) : (
          <span className="multi-select-empty">Ingen valgt</span>
        )}
      </div>
    </div>
  );
}

function sameSelection(a: string[], b: string[]) {
  return a.length === b.length && a.every((id) => b.includes(id));
}
