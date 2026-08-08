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

    const input = screen.getByRole('combobox');
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

describe('AddressAutocomplete focus handling', () => {
  it('keeps focus in the input when text is pasted', () => {
    const onTextChange = vi.fn();
    render(
      <AddressAutocomplete value="" onTextChange={onTextChange} onSelectSuggestion={vi.fn()} />,
    );

    const input = screen.getByRole('combobox');
    input.focus();
    fireEvent.change(input, { target: { value: 'Testvej 1' } });

    expect(document.activeElement).toBe(input);
    expect(onTextChange).toHaveBeenCalledWith('Testvej 1');
  });

  it('selects with the keyboard without moving DOM focus to a disappearing option', () => {
    const onSelectSuggestion = vi.fn();
    render(
      <AddressAutocomplete value="Test" onTextChange={vi.fn()} onSelectSuggestion={onSelectSuggestion} />,
    );

    const input = screen.getByRole('combobox');
    input.focus();
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(input).toHaveAttribute('aria-activedescendant');

    fireEvent.keyDown(input, { key: 'Enter' });

    expect(onSelectSuggestion).toHaveBeenCalledWith(expect.objectContaining({ street: 'Testvej 1' }));
    expect(document.activeElement).toBe(input);
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('preserves input focus when a suggestion is clicked', () => {
    const onSelectSuggestion = vi.fn();
    render(
      <AddressAutocomplete value="Test" onTextChange={vi.fn()} onSelectSuggestion={onSelectSuggestion} />,
    );

    const input = screen.getByRole('combobox');
    fireEvent.focus(input);
    const option = screen.getByRole('option');
    fireEvent.mouseDown(option);
    fireEvent.click(option);

    expect(document.activeElement).toBe(input);
    expect(onSelectSuggestion).toHaveBeenCalledOnce();
  });
});

