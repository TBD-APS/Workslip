import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CopyAddressButton } from './CopyAddressButton';

const notify = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
}));

vi.mock('../lib/toast', () => ({ notify }));

describe('CopyAddressButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: undefined,
    });
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: undefined,
    });
  });

  it('copies the normalized address with the Clipboard API', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });

    render(<CopyAddressButton address="  Vesterbrogade 100, 1620 København V  " />);
    fireEvent.click(screen.getByRole('button', { name: 'Kopiér adresse' }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('Vesterbrogade 100, 1620 København V'));
    expect(notify.success).toHaveBeenCalledWith('Adresse kopieret');
    expect(screen.getByRole('button', { name: 'Adresse kopieret' })).toBeInTheDocument();
  });

  it('falls back to selection-based copying when the Clipboard API is unavailable', async () => {
    const execCommand = vi.fn().mockReturnValue(true);
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: execCommand,
    });

    render(<CopyAddressButton address="Nørrebrogade 1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Kopiér adresse' }));

    await waitFor(() => expect(execCommand).toHaveBeenCalledWith('copy'));
    expect(notify.success).toHaveBeenCalledWith('Adresse kopieret');
  });

  it('reports an error when neither copy mechanism succeeds', async () => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockRejectedValue(new Error('denied')) },
    });
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: vi.fn().mockReturnValue(false),
    });

    render(<CopyAddressButton address="Nørrebrogade 1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Kopiér adresse' }));

    await waitFor(() => expect(notify.error).toHaveBeenCalledWith('Adressen kunne ikke kopieres. Prøv igen.'));
    expect(notify.success).not.toHaveBeenCalled();
  });

  it('does not render for an empty address', () => {
    render(<CopyAddressButton address="   " />);
    expect(screen.queryByRole('button', { name: 'Kopiér adresse' })).not.toBeInTheDocument();
  });
});
