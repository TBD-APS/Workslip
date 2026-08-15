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

type JobSearchResponse = { items: JobListItemViewModel[]; totalCount: number };

interface QuickNavigatorProps {
  isOpen: boolean; onOpen: () => void; onClose: () => void; homePath: string; homeLabel: string;
  canUseAppCommands: boolean; canSearchJobs: boolean; canViewAllJobs: boolean; currentUserId?: string;
  canViewTimer: boolean; canManageUsers: boolean; canViewCustomers: boolean; canViewDocs: boolean;
  canEditCustomers: boolean; canCreateJobs: boolean; canManageOrganization: boolean; showProfile: boolean;
}

const normalize = (value: string) => value.trim().toLocaleLowerCase('da-DK');
const isReadonlyState = (status: JobStatus) => status === JobStatus.InReview || status === JobStatus.Approved;

export function QuickNavigator(props: QuickNavigatorProps) {
  const { isOpen, onOpen, onClose, homePath, homeLabel, canUseAppCommands, canSearchJobs, canViewAllJobs, currentUserId, canViewTimer, canManageUsers, canViewCustomers, canViewDocs, canEditCustomers, canCreateJobs, canManageOrganization, showProfile } = props;
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

  const commands = useMemo(() => buildQuickNavigatorCommands({ homePath, homeLabel, canUseAppCommands, canViewTimer, canManageUsers, canViewCustomers, canViewDocs, canEditCustomers, canCreateJobs, canManageOrganization, showProfile }), [canCreateJobs, canEditCustomers, canManageOrganization, canManageUsers, canUseAppCommands, canViewCustomers, canViewDocs, canViewTimer, homeLabel, homePath, showProfile]);
  const hasSearchQuery = normalize(query).length > 0;
  const filteredCommands = useMemo(() => {
    const needle = normalize(query);
    if (!needle) return [];
    return commands.filter((command) => [command.label, command.description, ...command.keywords].some((value) => normalize(value).includes(needle))).slice(0, 4);
  }, [commands, query]);

  const jobSearchTerm = getQuickJobSearchTerm(query);
  const hasCurrentJobResults = Boolean(jobSearchTerm) && jobResultTerm === jobSearchTerm;
  const visibleJobs = hasCurrentJobResults ? jobs : [];
  const results: QuickNavigatorResult[] = [...visibleJobs.map((job) => ({ type: 'job' as const, job })), ...filteredCommands.map((command) => ({ type: 'command' as const, command }))];
  const safeActiveIndex = Math.min(activeIndex, Math.max(results.length - 1, 0));
  const isPendingJobSearch = Boolean(jobSearchTerm) && jobResultTerm !== jobSearchTerm;
  const showSearchingJobs = Boolean(jobSearchTerm) && (isPendingJobSearch || isSearchingJobs);
  const showJobSearchError = hasCurrentJobResults && jobSearchFailed;

  const resetAndClose = () => { setQuery(''); setJobs([]); setJobResultTerm(''); setJobSearchFailed(false); setIsSearchingJobs(false); setActiveIndex(0); onClose(); };

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => { if ((event.metaKey || event.ctrlKey) && event.key.toLocaleLowerCase() === 'k') { event.preventDefault(); onOpen(); } };
    window.addEventListener('keydown', handleShortcut); return () => window.removeEventListener('keydown', handleShortcut);
  }, [onOpen]);

  useEffect(() => {
    if (!isOpen) return undefined;
    previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow; document.body.style.overflow = 'hidden';
    const frame = window.requestAnimationFrame(() => inputRef.current?.focus());
    return () => { window.cancelAnimationFrame(frame); document.body.style.overflow = previousOverflow; previousFocusRef.current?.focus(); };
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen || !canSearchJobs || !jobSearchTerm) return undefined;
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setJobResultTerm(jobSearchTerm); setJobs([]); setIsSearchingJobs(true); setJobSearchFailed(false);
      try {
        const response = await apiClient.get('/api/jobs', { params: { search: jobSearchTerm, limit: 6, offset: 0 }, signal: controller.signal }) as JobSearchResponse;
        setJobs(filterQuickNavigationJobs(response.items ?? [], canViewAllJobs, currentUserId));
      } catch { if (!controller.signal.aborted) { setJobs([]); setJobSearchFailed(true); } }
      finally { if (!controller.signal.aborted) setIsSearchingJobs(false); }
    }, 180);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [canSearchJobs, canViewAllJobs, currentUserId, isOpen, jobSearchTerm]);

  if (!isOpen) return null;

  const selectResult = (result: QuickNavigatorResult) => {
    if (result.type === 'command') { resetAndClose(); navigate(result.command.path); return; }
    const path = isReadonlyState(result.job.status) ? `/app/completed/${result.job.id}` : `/app/job/${result.job.id}`;
    const from = `${location.pathname}${location.search}${location.hash}`; resetAndClose(); navigate(path, { state: { from } });
  };

  const handleInputKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') { event.preventDefault(); setActiveIndex(results.length === 0 ? 0 : (safeActiveIndex + 1) % results.length); return; }
    if (event.key === 'ArrowUp') { event.preventDefault(); setActiveIndex(results.length === 0 ? 0 : (safeActiveIndex - 1 + results.length) % results.length); return; }
    if (event.key === 'Enter' && results[safeActiveIndex]) { event.preventDefault(); selectResult(results[safeActiveIndex]); }
  };

  const handleDialogKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') { event.preventDefault(); resetAndClose(); return; }
    if (event.key !== 'Tab') return;
    const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), [href], [tabindex]:not([tabindex="-1"])');
    if (!focusable?.length) return;
    const first = focusable[0]; const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  };

  const resultCountText = results.length === 0 ? 'Ingen resultater' : `${results.length} ${results.length === 1 ? 'resultat' : 'resultater'}`;

  return <div className="quick-nav-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) resetAndClose(); }}>
    <div ref={dialogRef} className="quick-nav-dialog" role="dialog" aria-modal="true" aria-labelledby="quick-nav-title" onKeyDown={handleDialogKeyDown}>
      <div className="quick-nav-header"><div><div className="quick-nav-kicker">Søg i Workslip</div><h2 id="quick-nav-title">Find en sag</h2><p>Indtast sagsnummer, kunde eller adresse.</p></div><button type="button" className="quick-nav-close" onClick={resetAndClose} aria-label="Luk søgning"><X size={18} /></button></div>
      <div className="quick-nav-search-wrap"><Search size={20} aria-hidden="true" /><input ref={inputRef} type="search" value={query} onChange={(event) => { setQuery(event.target.value); setActiveIndex(0); }} onKeyDown={handleInputKeyDown} placeholder="Fx SAG-1042, Jensen eller Vestergade" aria-label="Søg i Workslip" autoComplete="off" spellCheck={false} />{query && <button type="button" className="quick-nav-clear" onClick={() => { setQuery(''); setJobs([]); setJobResultTerm(''); setActiveIndex(0); inputRef.current?.focus(); }} aria-label="Ryd søgning"><X size={16} /></button>}</div>
      <div className="quick-nav-meta" aria-live="polite"><span>{hasSearchQuery ? resultCountText : 'Søg direkte i dine sager'}</span>{showSearchingJobs && <span className="quick-nav-searching"><LoaderCircle size={14} /> Søger…</span>}</div>
      <div className="quick-nav-results">
        {!hasSearchQuery && <div className="quick-nav-empty quick-nav-empty--welcome"><Search size={24} aria-hidden="true" /><strong>Hvad leder du efter?</strong><span>Du kan søge på sagsnummer, kundenavn eller adresse.</span></div>}
        {results.map((result, index) => result.type === 'command' ? <button key={result.command.id} type="button" className={`quick-nav-result${index === safeActiveIndex ? ' active' : ''}`} onMouseEnter={() => setActiveIndex(index)} onClick={() => selectResult(result)}><span className="quick-nav-result-icon">{(() => { const Icon = result.command.icon; return <Icon size={19} />; })()}</span><span className="quick-nav-result-copy"><strong>{result.command.label}</strong><span>{result.command.description}</span></span><ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" /></button> : <button key={`job-${result.job.id}`} type="button" className={`quick-nav-result quick-nav-job-result${index === safeActiveIndex ? ' active' : ''}`} onMouseEnter={() => setActiveIndex(index)} onClick={() => selectResult(result)}><span className="quick-nav-result-icon"><FileText size={19} /></span><span className="quick-nav-result-copy"><strong>{`SAG-${(result.job.reportNumber || result.job.id.slice(0, 4)).toUpperCase()}`}</strong><span>{result.job.customer?.name || result.job.taskDescription || 'Sag'}{(result.job.destinationAddress || result.job.customer?.address) ? ` · ${result.job.destinationAddress || result.job.customer?.address}` : ''}</span></span><ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" /></button>)}
        {hasSearchQuery && hasCurrentJobResults && !isSearchingJobs && results.length === 0 && !jobSearchFailed && <div className="quick-nav-empty"><Search size={22} aria-hidden="true" /><strong>Ingen sager fundet</strong><span>Prøv sagsnummer, kundenavn eller en del af adressen.</span></div>}
        {showJobSearchError && <div className="quick-nav-search-error" role="status">Sager kunne ikke søges lige nu. Prøv igen om et øjeblik.</div>}
      </div>
      <div className="quick-nav-footer"><span><kbd>↑</kbd><kbd>↓</kbd> vælg</span><span><kbd>Enter</kbd> åbn</span><span className="quick-nav-shortcut"><kbd>Esc</kbd> luk</span></div>
    </div>
  </div>;
}
