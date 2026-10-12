import { useCallback, useRef, useState } from 'react';

type AdressevaelgerHit = {
  type?: string;
  titel?: string;
  adressebetegnelse?: string;
  vejnavn?: string;
  vejNavn?: string;
  husnummer?: string;
  husNummer?: string;
  postnr?: string;
  postdistrikt?: string;
  postnummer?: { postnr?: string; navn?: string };
};

type AdressevaelgerResponse = {
  status?: string;
  beskrivelse?: string;
  fund?: AdressevaelgerHit[];
};

export type AddressSuggestion = {
  display: string;
  street: string;
  zipCode: string;
  city: string;
};

const ADDRESS_SEARCH_URL = 'https://adressevaelger.dk/adresser/soeg';
const ADDRESS_TOKEN = 'adressevaelger123';
const DEBOUNCE_MS = 250;
const MIN_QUERY_LENGTH = 3;
const MAX_SUGGESTIONS = 10;

function mapSuggestion(hit: AdressevaelgerHit): AddressSuggestion | null {
  const display = hit.titel?.trim() || hit.adressebetegnelse?.trim();
  if (!display) return null;

  // Adressevælgerens label indeholder den fulde betegnelse for konkrete adresser.
  // Det sidste kommafelt er postnummer/by; for vejforslag er der ingen sådan del.
  const postalMatch = display.match(/,\\s*(\\d{4})\\s+(.+)$/);
  const streetFromLabel = postalMatch
    ? display.slice(0, postalMatch.index).trim()
    : display;
  const streetName = hit.vejnavn ?? hit.vejNavn;
  const houseNumber = hit.husnummer ?? hit.husNummer;
  const street = streetName
    ? [streetName, houseNumber].filter(Boolean).join(' ')
    : streetFromLabel;

  return {
    display,
    street,
    zipCode: hit.postnr ?? hit.postnummer?.postnr ?? postalMatch?.[1] ?? '',
    city: hit.postdistrikt ?? hit.postnummer?.navn ?? postalMatch?.[2] ?? '',
  };
}

export function useAddressAutocomplete() {
  const [suggestions, setSuggestions] = useState<AddressSuggestion[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [searchError, setSearchError] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const abortRef = useRef<AbortController | undefined>(undefined);
  const requestIdRef = useRef(0);

  const search = useCallback((query: string) => {
    clearTimeout(timerRef.current);
    abortRef.current?.abort();
    const requestId = ++requestIdRef.current;
    setSuggestions([]);
    setSearchError(false);

    if (query.trim().length < MIN_QUERY_LENGTH) {
      setIsLoading(false);
      return;
    }

    timerRef.current = setTimeout(async () => {
      const controller = new AbortController();
      abortRef.current = controller;
      setIsLoading(true);

      try {
        const params = new URLSearchParams({
          tekst: query.trim(),
          token: ADDRESS_TOKEN,
          maksimum: String(MAX_SUGGESTIONS),
        });
        const response = await fetch(`${ADDRESS_SEARCH_URL}?${params.toString()}`, {
          signal: controller.signal,
        });
        if (!response.ok) throw new Error(`Adressevælger returned ${response.status}`);

        const data = (await response.json()) as AdressevaelgerResponse;
        if (data.status !== 'ok' || !Array.isArray(data.fund)) {
          throw new Error(data.beskrivelse || 'Ugyldigt svar fra Adressevælger');
        }

        if (requestId !== requestIdRef.current || controller.signal.aborted) return;
        const mapped = data.fund
          .map(mapSuggestion)
          .filter((item): item is AddressSuggestion => item !== null)
          .filter((item, index, all) => all.findIndex((candidate) => candidate.display === item.display) === index);
        setSuggestions(mapped);
      } catch {
        if (requestId === requestIdRef.current && !controller.signal.aborted) {
          setSuggestions([]);
          setSearchError(true);
        }
      } finally {
        if (requestId === requestIdRef.current) setIsLoading(false);
      }
    }, DEBOUNCE_MS);
  }, []);

  const clear = useCallback(() => {
    clearTimeout(timerRef.current);
    abortRef.current?.abort();
    requestIdRef.current += 1;
    setSuggestions([]);
    setIsLoading(false);
    setSearchError(false);
  }, []);

  return { suggestions, isLoading, searchError, search, clear };
}
