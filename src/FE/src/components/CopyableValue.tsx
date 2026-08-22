import { Check, ChevronDown, Copy, Mail, MapPin, Phone } from 'lucide-react';
import {
  type KeyboardEvent,
  type MouseEvent,
  type ReactNode,
  useEffect,
  useRef,
  useState,
} from 'react';
import { copyTextToClipboard } from '../lib/clipboard';
import {
  type DomainFieldKey,
  getCallHref,
  getCopySuccessMessage,
  getDomainFieldPolicy,
  getEmailHref,
  normalizeDomainValue,
} from '../lib/copyableFields';
import { notify } from '../lib/toast';
import { getAddressMapsUrl } from './addressActionsUtils';
import './CopyableValue.css';

export type DomainValueProps = {
  field: DomainFieldKey;
  value: string | null | undefined;
  children?: ReactNode;
  className?: string;
  id?: string;
};

export function DomainValue({ field, value, children, className, id }: DomainValueProps) {
  const [copied, setCopied] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const resetTimerRef = useRef<number | null>(null);
  const rootRef = useRef<HTMLSpanElement | null>(null);
  const normalizedValue = normalizeDomainValue(field, value);
  const policy = getDomainFieldPolicy(field);
  const hasMultipleActions = policy.actions.length > 1;

  useEffect(() => () => {
    if (resetTimerRef.current !== null) {
      window.clearTimeout(resetTimerRef.current);
    }
  }, []);

  useEffect(() => {
    if (!menuOpen) return undefined;

    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setMenuOpen(false);
      }
    };
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false);
    };

    document.addEventListener('pointerdown', closeOnOutsidePointer);
    document.addEventListener('keydown', closeOnEscape);
    return () => {
      document.removeEventListener('pointerdown', closeOnOutsidePointer);
      document.removeEventListener('keydown', closeOnEscape);
    };
  }, [menuOpen]);

  if (!normalizedValue) {
    return children ?? null;
  }

  if (policy.actions.length === 0) {
    return (
      <span
        id={id}
        className={className}
        data-domain-field={field}
        data-copyable="false"
      >
        {children ?? value}
      </span>
    );
  }

  const copy = async () => {
    try {
      await copyTextToClipboard(normalizedValue);
      setCopied(true);
      setMenuOpen(false);
      notify.success(getCopySuccessMessage(field));

      if (resetTimerRef.current !== null) {
        window.clearTimeout(resetTimerRef.current);
      }
      resetTimerRef.current = window.setTimeout(() => setCopied(false), 1500);
    } catch {
      notify.error(`${policy.label} kunne ikke kopieres. Prøv igen.`);
    }
  };

  const handleDirectCopyClick = (event: MouseEvent<HTMLSpanElement>) => {
    event.stopPropagation();
    void copy();
  };

  const handleDirectCopyKeyDown = (event: KeyboardEvent<HTMLSpanElement>) => {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    event.stopPropagation();
    void copy();
  };

  if (!hasMultipleActions && policy.actions[0] === 'copy') {
    return (
      <span
        id={id}
        className={`copyable-value${copied ? ' is-copied' : ''}${className ? ` ${className}` : ''}`}
        role="button"
        tabIndex={0}
        data-domain-field={field}
        data-copy-field={field}
        data-copyable="true"
        onClick={handleDirectCopyClick}
        onKeyDown={handleDirectCopyKeyDown}
        aria-label={`Kopiér ${policy.label.toLowerCase()}: ${normalizedValue}`}
        title={copied ? `${policy.label} kopieret` : `Kopiér ${policy.label.toLowerCase()}`}
      >
        <span className="copyable-value-text">{children ?? value}</span>
        <span className="copyable-value-indicator" aria-hidden="true">
          {copied ? <Check size={14} /> : <Copy size={14} />}
        </span>
        <span className="sr-only" aria-live="polite">{copied ? `${policy.label} kopieret` : ''}</span>
      </span>
    );
  }

  return (
    <span
      ref={rootRef}
      className={`domain-value-actions${className ? ` ${className}` : ''}`}
      data-domain-field={field}
      data-copyable={policy.copyable ? 'true' : 'false'}
      onClick={(event) => event.stopPropagation()}
    >
      <button
        id={id}
        type="button"
        className={`copyable-value domain-value-action-trigger${copied ? ' is-copied' : ''}`}
        aria-haspopup="menu"
        aria-expanded={menuOpen}
        aria-label={`Handlinger for ${policy.label.toLowerCase()}: ${normalizedValue}`}
        title={`Vis handlinger for ${policy.label.toLowerCase()}`}
        onClick={(event) => {
          event.stopPropagation();
          setMenuOpen((current) => !current);
        }}
      >
        <span className="copyable-value-text">{children ?? value}</span>
        <span className="copyable-value-indicator domain-value-action-indicator" aria-hidden="true">
          {copied ? <Check size={14} /> : <ChevronDown size={14} />}
        </span>
      </button>

      {menuOpen && (
        <span className="domain-value-action-menu" role="menu" aria-label={`Handlinger for ${policy.label.toLowerCase()}`}>
          {policy.actions.includes('copy') && (
            <button
              id={id ? `${id}-copy` : undefined}
              type="button"
              className="domain-value-action-item"
              role="menuitem"
              onClick={(event) => {
                event.stopPropagation();
                void copy();
              }}
            >
              <Copy size={16} aria-hidden="true" />
              Kopiér
            </button>
          )}
          {policy.actions.includes('call') && (
            <a
              id={id ? `${id}-call` : undefined}
              className="domain-value-action-item"
              role="menuitem"
              href={getCallHref(normalizedValue)}
              onClick={(event) => event.stopPropagation()}
            >
              <Phone size={16} aria-hidden="true" />
              Ring op
            </a>
          )}
          {policy.actions.includes('email') && (
            <a
              id={id ? `${id}-email` : undefined}
              className="domain-value-action-item"
              role="menuitem"
              href={getEmailHref(normalizedValue)}
              onClick={(event) => event.stopPropagation()}
            >
              <Mail size={16} aria-hidden="true" />
              Send mail
            </a>
          )}
          {policy.actions.includes('maps') && (
            <a
              id={id ? `${id}-maps` : undefined}
              className="domain-value-action-item"
              role="menuitem"
              href={getAddressMapsUrl(normalizedValue)}
              target="_blank"
              rel="noreferrer"
              onClick={(event) => event.stopPropagation()}
            >
              <MapPin size={16} aria-hidden="true" />
              Åbn i Maps
            </a>
          )}
        </span>
      )}
      <span className="sr-only" aria-live="polite">{copied ? `${policy.label} kopieret` : ''}</span>
    </span>
  );
}

// Backwards-compatible export for the current WOR-724 migration. New policy-controlled
// UI values should prefer the DomainValue name because copyability/actions are policy driven.
export const CopyableValue = DomainValue;
export type CopyableValueProps = DomainValueProps;
