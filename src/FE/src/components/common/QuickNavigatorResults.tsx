import { ArrowRight, BookOpen, Building2, FileText, Search } from 'lucide-react';
import type { ReactNode } from 'react';
import type { QuickNavigatorResult } from './QuickNavigator';
import type { QuickNavigatorSearchResult } from './quickNavigatorTypes';

interface QuickNavigatorResultsProps {
  results: QuickNavigatorResult[];
  query: string;
  safeActiveIndex: number;
  hasSearchQuery: boolean;
  searchResult: QuickNavigatorSearchResult;
  onSelect: (result: QuickNavigatorResult) => void;
  onHoverIndex: (index: number) => void;
}

function getHighlightTerm(query: string): string {
  return query.trim().replace(/^(sag|job|kunde|doc|docs|dokument)\b\s*#?\s*/i, '').trim();
}

function highlightMatch(value: string, query: string): ReactNode {
  const term = getHighlightTerm(query);
  if (!term) return value;
  const index = value.toLocaleLowerCase('da-DK').indexOf(term.toLocaleLowerCase('da-DK'));
  if (index < 0) return value;
  return (
    <>
      {value.slice(0, index)}
      <mark className="quick-nav-match">{value.slice(index, index + term.length)}</mark>
      {value.slice(index + term.length)}
    </>
  );
}

export function QuickNavigatorResults({
  results,
  query,
  safeActiveIndex,
  hasSearchQuery,
  searchResult,
  onSelect,
  onHoverIndex,
}: QuickNavigatorResultsProps) {
  const {
    isLoadingJobs,
    isLoadingCustomers,
    isLoadingDocuments,
    jobError,
    customerError,
    documentError,
    jobs,
    customers,
    documents,
  } = searchResult;

  const anyLoading = isLoadingJobs || isLoadingCustomers || isLoadingDocuments;
  const anyError = jobError || customerError || documentError;
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
                <strong>{highlightMatch(result.command.label, query)}</strong>
                <span>{highlightMatch(result.command.description, query)} · Funktion</span>
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
                <strong>{highlightMatch(`${title} · ${customer}`, query)}</strong>
                <span>{address ? <>{highlightMatch(address, query)} · </> : null}Sag</span>
              </span>
              <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
            </button>
          );
        }

        if (result.type === 'customer') {
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
                <strong>{highlightMatch(customer.name, query)}</strong>
                <span>{customer.address ? <>{highlightMatch(customer.address, query)} · </> : null}Kunde</span>
              </span>
              <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
            </button>
          );
        }

        const document = result.document;
        const documentMeta = document.tags.length > 0
          ? `${document.tags.slice(0, 2).join(' · ')} · Dokument`
          : 'Dokument';
        return (
          <button
            key={`document-${document.id}`}
            type="button"
            className={`quick-nav-result quick-nav-document-result${isActive ? ' active' : ''}`}
            onMouseEnter={() => onHoverIndex(index)}
            onClick={() => onSelect(result)}
          >
            <span className="quick-nav-result-icon"><BookOpen size={19} /></span>
            <span className="quick-nav-result-copy">
              <strong>{highlightMatch(document.title, query)}</strong>
              <span>{highlightMatch(document.preview || documentMeta, query)}{document.preview ? ' · Dokument' : ''}</span>
            </span>
            <ArrowRight size={17} className="quick-nav-result-arrow" aria-hidden="true" />
          </button>
        );
      })}

      {!hasSearchQuery && !hasAnyResults && (
        <div className="quick-nav-empty">
          <Search size={22} aria-hidden="true" />
          <strong>Find det, du leder efter</strong>
          <span>Søg på navn, adresse, sagsnummer eller dokument.</span>
        </div>
      )}

      {hasSearchQuery && !hasAnyResults && !anyLoading && !anyError && (
        <div className="quick-nav-empty">
          <Search size={22} aria-hidden="true" />
          <strong>Ingen resultater</strong>
          <span>Prøv navn, adresse, sagsnummer eller et andet ord.</span>
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

      {documentError && documents.length === 0 && (
        <div className="quick-nav-search-error" role="status">
          Dokumenter kunne ikke søges lige nu.
        </div>
      )}
    </div>
  );
}
