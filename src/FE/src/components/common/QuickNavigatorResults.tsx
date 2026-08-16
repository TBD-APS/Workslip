import { ArrowRight, Building2, FileText, Search } from 'lucide-react';
import type { QuickNavigatorResult } from './QuickNavigator';
import type { QuickNavigatorSearchResult } from './quickNavigatorTypes';

interface QuickNavigatorResultsProps {
  results: QuickNavigatorResult[];
  safeActiveIndex: number;
  hasSearchQuery: boolean;
  searchResult: QuickNavigatorSearchResult;
  onSelect: (result: QuickNavigatorResult) => void;
  onHoverIndex: (index: number) => void;
}

export function QuickNavigatorResults({
  results,
  safeActiveIndex,
  hasSearchQuery,
  searchResult,
  onSelect,
  onHoverIndex,
}: QuickNavigatorResultsProps) {
  const {
    isLoadingJobs,
    isLoadingCustomers,
    jobError,
    customerError,
    jobSearchDegraded,
    customerSearchDegraded,
    jobs,
    customers,
  } = searchResult;

  const anyLoading = isLoadingJobs || isLoadingCustomers;
  const anyError = jobError || customerError;
  const hasAnyResults = results.length > 0;

  return (
    <div className="quick-nav-results">
      {results.map((result, index) => {
        const isActive = index === safeActiveIndex;

        if (result.type === 'command') {
          const Icon = result.command.icon;
          return (
            <button
              key={result.command.id}
              type="button"
              className={`quick-nav-result${isActive ? ' active' : ''}`}
              onMouseEnter={() => onHoverIndex(index)}
              onClick={() => onSelect(result)}
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

        if (result.type === 'job') {
          const job = result.job;
          const title = `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`;
          const customer = job.customer?.name || job.taskDescription || 'Sag';
          const address = job.destinationAddress || job.customer?.address;
          return (
            <button
              key={`job-${job.id}`}
              type="button"
              className={`quick-nav-result quick-nav-job-result${isActive ? ' active' : ''}`}
              onMouseEnter={() => onHoverIndex(index)}
              onClick={() => onSelect(result)}
            >
              <span className="quick-nav-result-icon"><FileText size={19} /></span>
              <span className="quick-nav-result-copy">
                <strong>{title} · {customer}</strong>
                <span>{address || 'Åbn sag'}</span>
              </span>
              <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
            </button>
          );
        }

        // customer
        const customer = result.customer;
        return (
          <button
            key={`customer-${customer.id}`}
            type="button"
            className={`quick-nav-result quick-nav-customer-result${isActive ? ' active' : ''}`}
            onMouseEnter={() => onHoverIndex(index)}
            onClick={() => onSelect(result)}
          >
            <span className="quick-nav-result-icon"><Building2 size={19} /></span>
            <span className="quick-nav-result-copy">
              <strong>{customer.name}</strong>
              <span>{customer.address || 'Åbn kunde'}</span>
            </span>
            <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
          </button>
        );
      })}

      {!hasSearchQuery && !hasAnyResults && (
        <div className="quick-nav-empty">
          <Search size={22} aria-hidden="true" />
          <strong>Ingen resultater</strong>
          <span>Prøv fx "timer", "docs", "kunde" eller "sag 1234".</span>
        </div>
      )}

      {hasSearchQuery && !hasAnyResults && !anyLoading && !anyError && (
        <div className="quick-nav-empty">
          <Search size={22} aria-hidden="true" />
          <strong>Ingen resultater</strong>
          <span>Prøv et andet søgeord.</span>
        </div>
      )}

      {jobError && jobs.length === 0 && (
        <div className="quick-nav-search-error" role="status">
          Sager kunne ikke søges lige nu.
        </div>
      )}

      {customerError && customers.length === 0 && (
        <div className="quick-nav-search-error" role="status">
          Kunder kunne ikke søges lige nu.
        </div>
      )}

      {jobSearchDegraded && (
        <div className="quick-nav-search-error" role="status">
          Søgning efter sager er midlertidigt nedsat. Viser tidligere resultater.
        </div>
      )}

      {customerSearchDegraded && (
        <div className="quick-nav-search-error" role="status">
          Søgning efter kunder er midlertidigt nedsat. Viser tidligere resultater.
        </div>
      )}
    </div>
  );
}
