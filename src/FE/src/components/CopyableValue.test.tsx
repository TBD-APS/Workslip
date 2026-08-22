import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CopyableValue, DomainValue } from './CopyableValue';
import { notify } from '../lib/toast';

vi.mock('../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

describe('DomainValue field interaction policy', () => {
  const writeText = vi.fn<() => Promise<void>>();

  beforeEach(() => {
    vi.clearAllMocks();
    writeText.mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });
  });

  it('copies a single-action value on click and stops parent navigation', async () => {
    const onParentClick = vi.fn();
    render(
      <div onClick={onParentClick}>
        <DomainValue field="customer.name" value="  Niels   Petersen  " />
      </div>,
    );

    fireEvent.click(screen.getByRole('button', { name: /kopiér kundenavn/i }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('Niels Petersen'));
    expect(onParentClick).not.toHaveBeenCalled();
    expect(notify.success).toHaveBeenCalledWith('Kundenavn kopieret');
  });

  it('renders an explicit non-interactive policy as plain text', () => {
    render(<DomainValue field="user.role" value="Admin" />);

    const value = screen.getByText('Admin');
    expect(value).toHaveAttribute('data-domain-field', 'user.role');
    expect(value).toHaveAttribute('data-copyable', 'false');
    expect(value).not.toHaveAttribute('role', 'button');
    expect(value).not.toHaveAttribute('tabindex');
    fireEvent.click(value);
    expect(writeText).not.toHaveBeenCalled();
  });

  it('supports Enter and Space keyboard activation for a direct-copy field', async () => {
    render(<DomainValue field="user.name" value="Worker" />);
    const target = screen.getByRole('button', { name: /kopiér medarbejdernavn/i });

    fireEvent.keyDown(target, { key: 'Enter' });
    fireEvent.keyDown(target, { key: ' ' });

    await waitFor(() => expect(writeText).toHaveBeenCalledTimes(2));
  });

  it('opens centrally defined phone actions with copy and call', async () => {
    render(<DomainValue id="test-phone" field="customer.phone" value="+45 12 34 56 78" />);

    fireEvent.click(screen.getByRole('button', { name: /handlinger for telefonnummer/i }));

    const copy = screen.getByRole('menuitem', { name: 'Kopiér' });
    const call = screen.getByRole('menuitem', { name: 'Ring op' });
    expect(call).toHaveAttribute('href', 'tel:+4512345678');

    fireEvent.click(copy);
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('+45 12 34 56 78'));
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('opens centrally defined e-mail actions with copy and send mail', () => {
    render(<DomainValue id="test-email" field="customer.email" value="kunde@example.dk" />);

    fireEvent.click(screen.getByRole('button', { name: /handlinger for e-mail/i }));

    expect(screen.getByRole('menuitem', { name: 'Kopiér' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Send mail' })).toHaveAttribute(
      'href',
      'mailto:kunde@example.dk',
    );
  });

  it('does not make an empty value interactive', () => {
    const { container } = render(<DomainValue field="customer.phone" value="   " />);
    expect(container).toBeEmptyDOMElement();
  });

  it('does not report success when a menu copy action fails', async () => {
    writeText.mockRejectedValue(new Error('denied'));
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: vi.fn(() => false),
    });

    render(<DomainValue field="customer.phone" value="12345678" />);
    fireEvent.click(screen.getByRole('button', { name: /handlinger for telefonnummer/i }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Kopiér' }));

    await waitFor(() => expect(notify.error).toHaveBeenCalled());
    expect(notify.success).not.toHaveBeenCalled();
  });

  it('keeps the CopyableValue export compatible during migration', () => {
    render(<CopyableValue field="customer.name" value="Kunde" />);
    expect(screen.getByRole('button', { name: /kopiér kundenavn/i })).toBeInTheDocument();
  });
});
