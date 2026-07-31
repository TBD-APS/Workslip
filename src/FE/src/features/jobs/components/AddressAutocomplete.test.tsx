import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddressAutocomplete } from './AddressAutocomplete';

const autocomplete = vi.hoisted(() => ({
  search: vi.fn(),
  clear: vi.fn(),
}));

vi.mock('../hooks/useAddressAutocomplete', () => ({
  useAddressAutocomplete: () => ({
    suggestions: [{
      display: 'Testvej 1, 8000 Aarhus C',
      street: 'Testvej 1',
      zipCode: '8000',
      city: 'Aarhus C',
    }],
    isLoading: false,
    search: autocomplete.search,
    clear: autocomplete.clear,
  }),
}));

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('AddressAutocomplete readonly mode', () => {
  it('prevents editing, clearing and opening suggestions', () => {
    const onTextChange = vi.fn();
    const onSelectSuggestion = vi.fn();
    const onClear = vi.fn();

    render(
      <AddressAutocomplete
        value="Testvej 1, 8000 Aarhus C"
        readOnly
        onTextChange={onTextChange}
        onSelectSuggestion={onSelectSuggestion}
        onClear={onClear}
      />,
    );

    const input = screen.getByRole('textbox');
    expect(input).toHaveAttribute('readonly');
    expect(screen.queryByTitle('Fjern adresse')).not.toBeInTheDocument();

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: 'Ny adresse' } });

    expect(autocomplete.search).not.toHaveBeenCalled();
    expect(onTextChange).not.toHaveBeenCalled();
    expect(onSelectSuggestion).not.toHaveBeenCalled();
    expect(onClear).not.toHaveBeenCalled();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});
