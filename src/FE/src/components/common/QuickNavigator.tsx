import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  ArrowRight,
  Building2,
  CalendarDays,
  ClipboardList,
  FileText,
  LoaderCircle,
  PlusCircle,
  Search,
  Settings,
  ShieldCheck,
  UserCircle,
  Users,
  X,
  type LucideIcon,
} from 'lucide-react';
import { JobStatus, type JobListItemViewModel } from '../../api/generated/models';
import { apiClient } from '../../lib/axios';
import './QuickNavigator.css';

type QuickNavigatorCommand = {
  id: string;
  label: string;
  description: string;
  path: string;
  keywords: string[];
  icon: LucideIcon;
};

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

const normalizeJobSearch = (value: string) =>
  value.trim().replace(/^(sag|job)\s*#?\s*/i, '').trim();

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
  const [isSearchingJobs, setIsSearchingJobs] = useState(false);
  const [jobSearchFailed, setJobSearchFailed] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);

  const commands = useMemo<QuickNavigatorCommand[]>(() => {
    const items: QuickNavigatorCommand[] = [];

    if (canUseAppCommands) {
      items.push({
        id: 'home',
        label: homeLabel,
        description: homeLabel === 'Rapporter' ? 'Åbn rapportoversigten' : 'Åbn sagsoversigten',
        path: homePath,
        keywords: ['hjem', 'oversigt', 'sag', 'sager', 'rapport', 'rapporter'],
        icon: ClipboardList,
      });
    }

    if (canViewTimer) {
      items.push({
        id: 'timer',
        label: 'Timer',
        description: 'Åbn timer og timesedler',
        path: '/app/timer',
        keywords: ['timer', 'tid', 'timeseddel', 'arbejdstid'],
        icon: CalendarDays,
      });
    }

    if (canManageUsers) {
      items.push({
        id: 'users',
        label: 'Folk',
        description: 'Åbn medarbejdere og brugere',
        path: '/app/users',
        keywords: ['folk', 'bruger', 'brugere', 'medarbejder', 'medarbejdere'],
        icon: Users,
      });
    }

    if (canViewCustomers) {
      items.push({
        id: 'customers',
        label: 'Kunder',
        description: 'Åbn kundelisten',
        path: '/app/customers',
        keywords: ['kunde', 'kunder', 'firma', 'virksomhed'],
        icon: Building2,
      });
    }

    if (canCreateJobs) {
      items.push({
        id: 'new-job',
        label: 'Opret sag',
        description: 'Start oprettelse af en ny sag',
        path: '/app/create',
        keywords: ['ny sag', 'opret sag', 'opgave', 'create'],
        icon: PlusCircle,
      });
    }

    if (canEditCustomers) {
      items.push({
        id: 'new-customer',
        label: 'Opret kunde',
        description: 'Opret en ny kunde',
        path: '/app/customers/new',
        keywords: ['ny kunde', 'opret kunde', 'firma', 'virksomhed'],
        icon: Building2,
      });
    }

    if (canManageUsers) {
      items.push({
        id: 'settings',
        label: 'Indstillinger',
        description: 'Åbn administrative indstillinger',
        path: '/app/settings',
        keywords: ['indstillinger', 'settings', 'administration', 'admin'],
        icon: Settings,
      });
    }

    if (showProfile) {
      items.push({
        id: 'profile',
        label: 'Profil',
        description: 'Åbn din profil',
        path: '/app/profil',
        keywords: ['profil', 'mig', 'konto'],
        icon: UserCircle,
      });
    }

    if (canManageOrganization) {
      items.push({
        id: 'superadmin',
        label: 'Superadmin',
        description: 'Åbn organisationsadministration',
        path: '/superadmin',
        keywords: ['superadmin', 'organisation', 'organization'],
        icon: ShieldCheck,
      });
    }

    return items;
  }, [
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

  const results = useMemo<QuickNavigatorResult[]>(() => [
    ...filteredCommands.map((command) => ({ type: 'command' as const, command })),
    ...jobs.map((job) => ({ type: 'job' as const, job })),
  ], [filteredCommands, jobs]);

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
    if (!isOpen) {
      setQuery('');
      setJobs([]);
      setJobSearchFailed(false);
      setIsSearchingJobs(false);
      setActiveIndex(0);
      return undefined;
    }

    const search = normalizeJobSearch(query);
    if (!canSearchJobs || search.length < 2) {
      setJobs([]);
      setJobSearchFailed(false);
      setIsSearchingJobs(false);
      return undefined;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setIsSearchingJobs(true);
      setJobSearchFailed(false);

      try {
        const response = await apiClient.get('/api/jobs', {
          params: { search, limit: 5, offset: 0 },
          signal: controller.signal,
        }) as JobSearchResponse;
        setJobs(response.items ?? []);
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
  }, [canSearchJobs, isOpen, query]);

  useEffect(() => {
    setActiveIndex((current) => Math.min(current, Math.max(results.length - 1, 0)));
  }, [results.length]);

  if (!isOpen) return null;

  const selectResult = (result: QuickNavigatorResult) => {
    if (result.type === 'command') {
      onClose();
      navigate(result.command.path);
      return;
    }

    const path = isReadonlyState(result.job.status)
      ? `/app/completed/${result.job.id}`
      : `/app/job/${result.job.id}`;
    const from = `${location.pathname}${location.search}${location.hash}`;
    onClose();
    navigate(path, { state: { from } });
  };

  const handleInputKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((current) => results.length === 0 ? 0 : (current + 1) % results.length);
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((current) => results.length === 0 ? 0 : (current - 1 + results.length) % results.length);
      return;
    }

    if (event.key === 'Enter' && results[activeIndex]) {
      event.preventDefault();
      selectResult(results[activeIndex]);
    }
  };

  const handleDialogKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      onClose();
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
        if (event.target === event.currentTarget) onClose();
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
          <button type="button" className="quick-nav-close" onClick={onClose} aria-label="Luk hurtig navigation">
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
          {isSearchingJobs && <span className="quick-nav-searching"><LoaderCircle size={14} /> Søger sager…</span>}
        </div>

        <div className="quick-nav-results">
          {results.map((result, index) => {
            if (result.type === 'command') {
              const Icon = result.command.icon;
              return (
                <button
                  key={result.command.id}
                  type="button"
                  className={`quick-nav-result${index === activeIndex ? ' active' : ''}`}
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
                className={`quick-nav-result quick-nav-job-result${index === activeIndex ? ' active' : ''}`}
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

          {!isSearchingJobs && results.length === 0 && (
            <div className="quick-nav-empty">
              <Search size={22} aria-hidden="true" />
              <strong>Ingen resultater</strong>
              <span>Prøv fx “timer”, “kunde” eller “sag 1234”.</span>
            </div>
          )}

          {jobSearchFailed && (
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
