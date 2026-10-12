import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useAddressAutocomplete } from './useAddressAutocomplete';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('useAddressAutocomplete', () => {
  it('searches Adressevælger and maps full address labels to Workslip fields', async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        status: 'ok',
        fund: [
          {
            type: 'adresse',
            id: 'address-1',
            titel: 'Fiskergade 2, st. tv, 8000 Aarhus C',
            vejnavn: 'Fiskergade',
            husnummer: '2',
          },
          { type: 'vejnavn', titel: 'Fiskergade', vejnavn: 'Fiskergade' },
        ],
      }),
    });
    vi.stubGlobal('fetch', fetchMock);
    const { result } = renderHook(() => useAddressAutocomplete());

    act(() => result.current.search('Fisk'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(250);
    });

    expect(fetchMock).toHaveBeenCalledOnce();
    const [requestUrl] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(requestUrl).toContain('https://adressevaelger.dk/adresser/soeg?');
    expect(requestUrl).toContain('tekst=Fisk');
    expect(requestUrl).toContain('token=adressevaelger123');
    expect(result.current.suggestions).toEqual([
      {
        display: 'Fiskergade 2, st. tv, 8000 Aarhus C',
        street: 'Fiskergade 2',
        zipCode: '8000',
        city: 'Aarhus C',
      },
      {
        display: 'Fiskergade',
        street: 'Fiskergade',
        zipCode: '',
        city: '',
      },
    ]);
    expect(result.current.searchError).toBe(false);
  });

  it('exposes provider errors so the component can offer manual entry and retry', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network unavailable')));
    const { result } = renderHook(() => useAddressAutocomplete());

    act(() => result.current.search('Fisk'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(250);
    });

    expect(result.current.suggestions).toEqual([]);
    expect(result.current.searchError).toBe(true);
  });

  it('ignores a stale response after a newer query starts', async () => {
    vi.useFakeTimers();
    let resolveFirst: ((value: unknown) => void) | undefined;
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve; }))
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          status: 'ok',
          fund: [{ type: 'adresse', titel: 'Fiskergade 2, 8000 Aarhus C', vejnavn: 'Fiskergade', husnummer: '2' }],
        }),
      });
    vi.stubGlobal('fetch', fetchMock);
    const { result } = renderHook(() => useAddressAutocomplete());

    act(() => result.current.search('Fis'));
    await act(async () => { await vi.advanceTimersByTimeAsync(250); });
    act(() => result.current.search('Fisk'));
    await act(async () => { await vi.advanceTimersByTimeAsync(250); });
    await act(async () => {
      resolveFirst?.({
        ok: true,
        json: async () => ({ status: 'ok', fund: [{ type: 'adresse', titel: 'Stalevej 1, 1000 København K' }] }),
      });
    });

    expect(result.current.suggestions[0]?.display).toBe('Fiskergade 2, 8000 Aarhus C');
  });
});
