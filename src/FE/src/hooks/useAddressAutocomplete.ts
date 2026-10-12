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

// Compatible DAR-backed endpoint. The government DAWA service shut down on 2026-10-01.
// This public endpoint is rate-limited; migrate to the official Adressevælger for scale.
const ADDRESS_API_BASE = 'https://dawa.companydata.dk/adresser/autocomplete';
const DEBOUNCE_MS = 250;
const MIN_QUERY_LENGTH = 3;

export function useAddressAutocomplete() {
  const [suggestions, setSuggestions] = useState<AddressSuggestion[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const abortRef = useRef<AbortController | undefined>(undefined);

  const search = useCallback((query: string) => {
    clearTimeout(timerRef.current);
    abortRef.current?.abort();
    setError(null);

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
          `${ADDRESS_API_BASE}?q=${encodeURIComponent(query)}&struktur=mini&per_side=10`,
          { signal: controller.signal },
        );
        if (!res.ok) throw new Error(`Address lookup failed (${res.status})`);

        const data: DawaAdresse[] = await res.json();
        if (!Array.isArray(data)) throw new Error('Unexpected address response');
        const mapped: AddressSuggestion[] = data.filter((item) => item?.adresse?.vejnavn).map((item) => {
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
          setError('Adresseforslag er midlertidigt utilgængelige. Du kan skrive adressen manuelt.');
        }
      } finally {
        if (!controller.signal.aborted) setIsLoading(false);
      }
    }, DEBOUNCE_MS);
  }, []);

  const clear = useCallback(() => {
    clearTimeout(timerRef.current);
    abortRef.current?.abort();
    setError(null);
    setSuggestions([]);
    setIsLoading(false);
  }, []);

  return { suggestions, isLoading, error, search, clear };
}
