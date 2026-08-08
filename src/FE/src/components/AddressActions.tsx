import { Check, Copy, Navigation } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { notify } from '../lib/toast';
import './AddressActions.css';

async function copyWithLegacyFallback(value: string): Promise<void> {
  const textarea = document.createElement('textarea');
  textarea.value = value;
  textarea.setAttribute('readonly', '');
  textarea.style.position = 'fixed';
  textarea.style.top = '0';
  textarea.style.left = '-9999px';
  textarea.style.opacity = '0';
  document.body.appendChild(textarea);

  try {
    textarea.focus();
    textarea.select();
    textarea.setSelectionRange(0, value.length);
    const copied = typeof document.execCommand === 'function' && document.execCommand('copy');
    if (!copied) {
      throw new Error('clipboard_unavailable');
    }
  } finally {
    textarea.remove();
  }
}

export async function copyTextToClipboard(value: string): Promise<void> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(value);
      return;
    } catch {
      // Safari and restricted browser contexts can expose the API while rejecting writes.
      // Fall through to the selection-based browser fallback before reporting an error.
    }
  }

  await copyWithLegacyFallback(value);
}

export function getAddressMapsUrl(address: string): string {
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
}

export type AddressActionsProps = {
  address: string | null | undefined;
  className?: string;
};

export function AddressActions({ address, className }: AddressActionsProps) {
  const [copied, setCopied] = useState(false);
  const resetTimerRef = useRef<number | null>(null);
  const normalizedAddress = address?.trim() ?? '';

  useEffect(() => () => {
    if (resetTimerRef.current !== null) {
      window.clearTimeout(resetTimerRef.current);
    }
  }, []);

  if (!normalizedAddress) return null;

  const handleCopy = async (event: React.MouseEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();

    try {
      await copyTextToClipboard(normalizedAddress);
      setCopied(true);
      notify.success('Adresse kopieret');

      if (resetTimerRef.current !== null) {
        window.clearTimeout(resetTimerRef.current);
      }
      resetTimerRef.current = window.setTimeout(() => setCopied(false), 1500);
    } catch {
      notify.error('Adressen kunne ikke kopieres. Prøv igen.');
    }
  };

  return (
    <span
      className={`address-actions${className ? ` ${className}` : ''}`}
      role="group"
      aria-label="Adressehandlinger"
      onClick={(event) => event.stopPropagation()}
    >
      <button
        type="button"
        className={`address-action address-action-copy${copied ? ' is-copied' : ''}`}
        onClick={(event) => { void handleCopy(event); }}
        aria-label={copied ? 'Adresse kopieret' : 'Kopiér adresse'}
        title={copied ? 'Adresse kopieret' : 'Kopiér adresse'}
      >
        {copied ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}
      </button>
      <a
        href={getAddressMapsUrl(normalizedAddress)}
        className="address-action address-action-maps"
        target="_blank"
        rel="noreferrer"
        aria-label="Åbn adresse i Google Maps"
        title="Åbn i Google Maps"
        onClick={(event) => event.stopPropagation()}
      >
        <Navigation size={16} aria-hidden="true" />
      </a>
    </span>
  );
}
