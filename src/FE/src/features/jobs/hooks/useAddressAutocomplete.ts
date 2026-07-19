import { useCallback, useRef, useState } from 'react';

type DawaAdgangsadresse = {
  vejnavn: string;
  husnr: string;
  etage: string | null;
  dør: string | null;
  postnr: string;
  postnrnavn: string;
};

type DawaAdresse = {
  tekst: string;
  adresse: DawaAdgangsadresse;
};

export type AddressSuggestion = {
  display: string;
  street: string;
  zipCode: string;
  city: string;
};

const DAWA_BASE = 'https://dawa.aws.dk/adresser/autocomplete';
const DEBOUNCE_MS = 250;
const MIN_QUERY_LENGTH = 3;

export function useAddressAutocomplete() {
  const [suggestions, setSuggestions] = useState<AddressSuggestion[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const abortRef = useRef<AbortController | undefined>(undefined);

  const search = useCallback((query: string) => {
    clearTimeout(timerRef.current);
    abortRef.current?.abort();

    if (query.trim().length < MIN_QUERY_LENGTH) {
      setSuggestions([]);
      setIsLoading(false);
      return;
    }

    timerRef.current = setTimeout(async () => {
      const controller = new AbortController();
      abortRef.current = controller;
      setIsLoading(true);

      try {
        const res = await fetch(
          `${DAWA_BASE}?q=${encodeURIComponent(query)}&struktur=mini`,
          { signal: controller.signal },
        );
        if (!res.ok) {
          setSuggestions([]);
          return;
        }

        const data: DawaAdresse[] = await res.json();
        const mapped: AddressSuggestion[] = data.map((item) => {
          const base = [item.adresse.vejnavn, item.adresse.husnr].filter(Boolean).join(' ');
          const floor = item.adresse.etage;
          const door = item.adresse.dør;
          const street = [base, floor && door ? `${floor}. ${door}` : floor ? `${floor}.` : door ? `. ${door}` : '']
            .filter(Boolean)
            .join(', ');

          return {
            display: item.tekst,
            street,
            zipCode: item.adresse.postnr,
            city: item.adresse.postnrnavn,
          };
        });

        setSuggestions(mapped);
      } catch {
        if (!controller.signal.aborted) {
          setSuggestions([]);
        }
      } finally {
        setIsLoading(false);
      }
    }, DEBOUNCE_MS);
  }, []);

  const clear = useCallback(() => {
    clearTimeout(timerRef.current);
    abortRef.current?.abort();
    setSuggestions([]);
    setIsLoading(false);
  }, []);

  return { suggestions, isLoading, search, clear };
}
