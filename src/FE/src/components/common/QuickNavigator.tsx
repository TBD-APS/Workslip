import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { ArrowRight, FileText, LoaderCircle, Search, X } from 'lucide-react';
import { JobStatus, type JobListItemViewModel } from '../../api/generated/models';
import { apiClient } from '../../lib/axios';
import { buildQuickNavigatorCommands, type QuickNavigatorCommand } from './quickNavigatorCommands';
import { filterQuickNavigationJobs, getQuickJobSearchTerm } from './quickNavigatorSearch';
import './QuickNavigator.css';

type QuickNavigatorResult =
  | { type: 'command'; command: QuickNavigatorCommand }
  | { type: 'job'; job: JobListItemViewModel };

type JobSearchResponse = {
  items: JobListItemViewModel[];
  totalCount: number;
};

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
  canEditCustomers: boolean;
  canCreateJobs: boolean;
  canManageOrganization: boolean;
  showProfile: boolean;
}

const normalize = (value: string) =>
  value.trim().toLocaleLowerCase('da-DK');

const isReadonlyState = (status: JobStatus) =>
  status === JobStatus.InReview || status === JobStatus.Approved;

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
  const [jobs, setJobs] = useState<JobListItemViewModel[]>([]);
  const [jobResultTerm, setJobResultTerm] = useState('');
  const [isSearchingJobs, setIsSearchingJobs] = useState(false);
  const [jobSearchFailed, setJobSearchFailed] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);

  const commands = useMemo(() => buildQuickNavigatorCommands({
    homePath,
    homeLabel,
    canUseAppCommands,
    canViewTimer,
    canManageUsers,
    canViewCustomers,
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

  const jobSearchTerm = getQuickJobSearchTerm(query);
  const hasCurrentJobResults = Boolean(jobSearchTerm) && jobResultTerm === jobSearchTerm;
  const visibleJobs = hasCurrentJobResults ? jobs : [];
  const results: QuickNavigatorResult[] = [
    ...filteredCommands.map((command) => ({ type: 'command' as const, command })),
    ...visibleJobs.map((job) => ({ type: 'job' as const, job })),
  ];
  const safeActiveIndex = Math.min(activeIndex, Math.max(results.length - 1, 0));
  const isPendingJobSearch = Boolean(jobSearchTerm) && jobResultTerm !== jobSearchTerm;
  const showSearchingJobs = Boolean(jobSearchTerm) && (isPendingJobSearch || isSearchingJobs);
  const showJobSearchError = hasCurrentJobResults && jobSearchFailed;

  const resetAndClose = () => {
    setQuery('');
    setJobs([]);
    setJobResultTerm('');
    setJobSearchFailed(false);
    setIsSearchingJobs(false);
    setActiveIndex(0);
    onClose();
  };

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLocaleLowerCase() === 'k') {
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

  useEffect(() => {
    if (!isOpen || !canSearchJobs || !jobSearchTerm) return undefined;

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setJobResultTerm(jobSearchTerm);
      setJobs([]);
      setIsSearchingJobs(true);
      setJobSearchFailed(false);

      try {
        const response = await apiClient.get('/api/jobs', {
          params: { search: jobSearchTerm, limit: 5, offset: 0 },
          signal: controller.signal,
        }) as JobSearchResponse;
        setJobs(filterQuickNavigationJobs(response.items ?? [], canViewAllJobs, currentUserId));
      } catch {
        if (!controller.signal.aborted) {
          setJobs([]);
          setJobSearchFailed(true);
        }
      } finally {
        if (!controller.signal.aborted) setIsSearchingJobs(false);
      }
    }, 180);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [canSearchJobs, canViewAllJobs, currentUserId, isOpen, jobSearchTerm]);

  if (!isOpen) return null;

  const selectResult = (result: QuickNavigatorResult) => {
    if (result.type === 'command') {
      resetAndClose();
      navigate(result.command.path);
      return;
    }

    const path = isReadonlyState(result.job.status)
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
          <div>
            <div className="quick-nav-kicker">Hurtig navigation</div>
            <h2 id="quick-nav-title">Hvor vil du hen?</h2>
          </div>
          <button type="button" className="quick-nav-close" onClick={resetAndClose} aria-label="Luk hurtig navigation">
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
            placeholder="Søg efter side eller sag…"
            aria-label="Søg i Workslip"
            autoComplete="off"
            spellCheck={false}
          />
          <kbd>Esc</kbd>
        </div>

        <div className="quick-nav-meta" aria-live="polite">
          <span>{hasSearchQuery ? resultCountText : 'Genveje'}</span>
          {showSearchingJobs && <span className="quick-nav-searching"><LoaderCircle size={14} /> Søger sager…</span>}
        </div>

        <div className="quick-nav-results">
          {results.map((result, index) => {
            if (result.type === 'command') {
              const Icon = result.command.icon;
              return (
                <button
                  key={result.command.id}
                  type="button"
                  className={`quick-nav-result${index === safeActiveIndex ? ' active' : ''}`}
                  onMouseEnter={() => setActiveIndex(index)}
                  onClick={() => selectResult(result)}
                >
                  <span className="quick-nav-result-icon"><Icon size={19} /></span>
                  <span className="quick-nav-result-copy">
                    <strong>{result.command.label}</strong>
                    <span>{result.command.description}</span>
                  </span>
                  <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
                </button>
              );
            }

            const job = result.job;
            const title = `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`;
            const customer = job.customer?.name || job.taskDescription || 'Sag';
            const address = job.destinationAddress || job.customer?.address;
            return (
              <button
                key={`job-${job.id}`}
                type="button"
                className={`quick-nav-result quick-nav-job-result${index === safeActiveIndex ? ' active' : ''}`}
                onMouseEnter={() => setActiveIndex(index)}
                onClick={() => selectResult(result)}
              >
                <span className="quick-nav-result-icon"><FileText size={19} /></span>
                <span className="quick-nav-result-copy">
                  <strong>{title} · {customer}</strong>
                  <span>{address || 'Åbn sag'}</span>
                </span>
                <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
              </button>
            );
          })}

          {!jobSearchTerm && results.length === 0 && (
            <div className="quick-nav-empty">
              <Search size={22} aria-hidden="true" />
              <strong>Ingen resultater</strong>
              <span>Prøv fx “timer”, “kunde” eller “sag 1234”.</span>
            </div>
          )}

          {hasCurrentJobResults && !isSearchingJobs && results.length === 0 && !jobSearchFailed && (
            <div className="quick-nav-empty">
              <Search size={22} aria-hidden="true" />
              <strong>Ingen sager fundet</strong>
              <span>Prøv et andet sagsnummer.</span>
            </div>
          )}

          {showJobSearchError && (
            <div className="quick-nav-search-error" role="status">
              Sager kunne ikke søges lige nu. Navigationen ovenfor virker stadig.
            </div>
          )}
        </div>

        <div className="quick-nav-footer">
          <span><kbd>↑</kbd><kbd>↓</kbd> vælg</span>
          <span><kbd>Enter</kbd> åbn</span>
          <span className="quick-nav-shortcut"><kbd>Ctrl</kbd>/<kbd>⌘</kbd><kbd>K</kbd></span>
        </div>
      </div>
    </div>
  );
}
