import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Search, X } from 'lucide-react';
import { buildQuickNavigatorCommands, type QuickNavigatorCommand } from './quickNavigatorCommands';
import { useQuickNavigatorSearch } from './useQuickNavigatorSearch';
import { QuickNavigatorResults } from './QuickNavigatorResults';
import type { QuickNavigatorSearchScope } from './quickNavigatorTypes';
import type {
  CustomerSearchViewModel,
  DocumentListItemResponse,
  JobListItemViewModel,
} from '../../api/generated/models';
import { JobStatus } from '../../api/generated/models';
import { toUiLowerCase } from '../../lib/presentation/text';
import './QuickNavigator.css';

export type QuickNavigatorResult =
  | { type: 'command'; command: QuickNavigatorCommand }
  | { type: 'job'; job: JobListItemViewModel }
  | { type: 'customer'; customer: CustomerSearchViewModel }
  | { type: 'document'; document: DocumentListItemResponse };

interface QuickNavigatorProps {
  isOpen: boolean;
  onOpen: () => void;
  onClose: () => void;
  homePath: string;
  homeLabel: string;
  canUseAppCommands: boolean;
  canSearchJobs: boolean;
  canViewAllJobs: boolean;
  currentUserId?: string;
  canViewTimer: boolean;
  canManageUsers: boolean;
  canViewCustomers: boolean;
  canViewDocs: boolean;
  canEditCustomers: boolean;
  canCreateJobs: boolean;
  canManageOrganization: boolean;
  showProfile: boolean;
}

const normalize = (value: string) => toUiLowerCase(value.trim());

export function QuickNavigator({
  isOpen,
  onOpen,
  onClose,
  homePath,
  homeLabel,
  canUseAppCommands,
  canSearchJobs,
  canViewAllJobs,
  currentUserId,
  canViewTimer,
  canManageUsers,
  canViewCustomers,
  canViewDocs,
  canEditCustomers,
  canCreateJobs,
  canManageOrganization,
  showProfile,
}: QuickNavigatorProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const inputRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);

  const commands = useMemo(() => buildQuickNavigatorCommands({
    homePath,
    homeLabel,
    canUseAppCommands,
    canViewTimer,
    canManageUsers,
    canViewCustomers,
    canViewDocs,
    canEditCustomers,
    canCreateJobs,
    canManageOrganization,
    showProfile,
  }), [
    canCreateJobs,
    canEditCustomers,
    canManageOrganization,
    canManageUsers,
    canUseAppCommands,
    canViewCustomers,
    canViewDocs,
    canViewTimer,
    homeLabel,
    homePath,
    showProfile,
  ]);

  const filteredCommands = useMemo(() => {
    const needle = normalize(query);
    if (!needle) return commands;
    return commands.filter((command) =>
      [command.label, command.description, ...command.keywords]
        .some((value) => normalize(value).includes(needle)),
    );
  }, [commands, query]);

  const searchScope: QuickNavigatorSearchScope = useMemo(() => ({
    canSearchJobs,
    canViewAllJobs,
    currentUserId,
    canViewCustomers,
    canViewDocs,
    query,
    isOpen,
  }), [canSearchJobs, canViewAllJobs, currentUserId, canViewCustomers, canViewDocs, query, isOpen]);

  const searchResult = useQuickNavigatorSearch(searchScope);

  const results: QuickNavigatorResult[] = useMemo(() => [
    ...filteredCommands.map((command) => ({ type: 'command' as const, command })),
    ...searchResult.jobs.map((job) => ({ type: 'job' as const, job })),
    ...searchResult.customers.map((customer) => ({ type: 'customer' as const, customer })),
    ...searchResult.documents.map((document) => ({ type: 'document' as const, document })),
  ], [filteredCommands, searchResult.jobs, searchResult.customers, searchResult.documents]);

  const safeActiveIndex = Math.min(activeIndex, Math.max(results.length - 1, 0));

  const resetAndClose = () => {
    setQuery('');
    setActiveIndex(0);
    onClose();
  };

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && toUiLowerCase(event.key) === 'k') {
        event.preventDefault();
        onOpen();
      }
    };
    window.addEventListener('keydown', handleShortcut);
    return () => window.removeEventListener('keydown', handleShortcut);
  }, [onOpen]);

  useEffect(() => {
    if (!isOpen) return undefined;
    previousFocusRef.current = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const frame = window.requestAnimationFrame(() => inputRef.current?.focus());
    return () => {
      window.cancelAnimationFrame(frame);
      document.body.style.overflow = previousOverflow;
      previousFocusRef.current?.focus();
    };
  }, [isOpen]);

  if (!isOpen) return null;

  const selectResult = (result: QuickNavigatorResult) => {
    if (result.type === 'command') {
      resetAndClose();
      navigate(result.command.path);
      return;
    }
    if (result.type === 'customer') {
      const from = `${location.pathname}${location.search}${location.hash}`;
      resetAndClose();
      navigate(`/app/customers/${result.customer.id}`, { state: { from } });
      return;
    }
    if (result.type === 'document') {
      const from = `${location.pathname}${location.search}${location.hash}`;
      resetAndClose();
      navigate(`/app/docs/${result.document.id}`, { state: { from } });
      return;
    }
    const path = result.job.status === JobStatus.InReview || result.job.status === JobStatus.Approved
      ? `/app/completed/${result.job.id}`
      : `/app/job/${result.job.id}`;
    const from = `${location.pathname}${location.search}${location.hash}`;
    resetAndClose();
    navigate(path, { state: { from } });
  };

  const handleInputKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex(results.length === 0 ? 0 : (safeActiveIndex + 1) % results.length);
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex(results.length === 0 ? 0 : (safeActiveIndex - 1 + results.length) % results.length);
      return;
    }
    if (event.key === 'Enter' && results[safeActiveIndex]) {
      event.preventDefault();
      selectResult(results[safeActiveIndex]);
    }
  };

  const handleDialogKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      resetAndClose();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = dialogRef.current?.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), [href], [tabindex]:not([tabindex="-1"])',
    );
    if (!focusable?.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  const hasSearchQuery = normalize(query).length > 0;
  const resultCountText = results.length === 0
    ? 'Ingen resultater'
    : `${results.length} ${results.length === 1 ? 'resultat' : 'resultater'}`;

  return (
    <div
      className="quick-nav-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) resetAndClose();
      }}
    >
      <div
        ref={dialogRef}
        className="quick-nav-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="quick-nav-title"
        onKeyDown={handleDialogKeyDown}
      >
        <div className="quick-nav-header">
          <h2 id="quick-nav-title">Søg</h2>
          <button type="button" className="quick-nav-close" onClick={resetAndClose} aria-label="Luk søgning">
            <X size={18} />
          </button>
        </div>

        <div className="quick-nav-search-wrap">
          <Search size={19} aria-hidden="true" />
          <input
            ref={inputRef}
            type="search"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setActiveIndex(0);
            }}
            onKeyDown={handleInputKeyDown}
            placeholder="Søg sag, kunde, adresse eller dokument…"
            aria-label="Søg i hele Workslip"
            autoComplete="off"
            spellCheck={false}
          />
          <kbd>Esc</kbd>
        </div>

        <div className="quick-nav-meta" aria-live="polite">
          <span>{hasSearchQuery ? resultCountText : 'Sager · kunder · adresser · docs · funktioner'}</span>
          {searchResult.isLoading && (
            <span className="quick-nav-searching">Søger…</span>
          )}
        </div>

        <QuickNavigatorResults
          results={results}
          query={query}
          safeActiveIndex={safeActiveIndex}
          hasSearchQuery={hasSearchQuery}
          searchResult={searchResult}
          onSelect={selectResult}
          onHoverIndex={setActiveIndex}
        />

        <div className="quick-nav-footer">
          <span><kbd>↑</kbd><kbd>↓</kbd> vælg</span>
          <span><kbd>Enter</kbd> åbn</span>
          <span className="quick-nav-shortcut"><kbd>Ctrl</kbd>/<kbd>⌘</kbd><kbd>K</kbd></span>
        </div>
      </div>
    </div>
  );
}
