import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OneTimeCodeLogin } from './OneTimeCodeLogin';

const mocks = vi.hoisted(() => ({
  login: vi.fn(),
  sendAuthCode: vi.fn(),
  clearReauthInFlight: vi.fn(),
}));

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({ login: mocks.login }),
}));

vi.mock('../api/devToken', () => ({
  sendAuthCode: mocks.sendAuthCode,
}));

vi.mock('../../../providers/authContextValue', () => ({
  clearReauthInFlight: mocks.clearReauthInFlight,
}));

vi.mock('./OneTimeCodeInput', () => ({
  OneTimeCodeInput: ({
    id,
    name,
    value,
    onValueChange,
    onBlur,
    disabled,
  }: {
    id?: string;
    name?: string;
    value: string;
    onValueChange: (value: string) => void;
    onBlur?: () => void;
    disabled?: boolean;
  }) => (
    <input
      id={id}
      name={name}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
      onBlur={onBlur}
      disabled={disabled}
    />
  ),
}));

function renderLogin(returnTo: string) {
  const loginUrl = `/login?returnTo=${encodeURIComponent(returnTo)}`;
  window.history.replaceState(null, '', loginUrl);

  render(
    <MemoryRouter initialEntries={[loginUrl]}>
      <Routes>
        <Route path="/login" element={<OneTimeCodeLogin onBack={vi.fn()} />} />
        <Route path="/app" element={<h1>App</h1>} />
        <Route path="/app/customers/:id" element={<h1>Kunde</h1>} />
      </Routes>
    </MemoryRouter>,
  );
}

async function completeLogin() {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'user@example.com' } });
  fireEvent.click(screen.getByRole('button', { name: 'Send kode' }));

  const code = await screen.findByLabelText('Engangskode');
  fireEvent.change(code, { target: { value: '123456' } });
  fireEvent.click(screen.getByRole('button', { name: 'Log ind' }));
}

beforeEach(() => {
  mocks.login.mockReset().mockResolvedValue(true);
  mocks.sendAuthCode.mockReset().mockResolvedValue(undefined);
  mocks.clearReauthInFlight.mockReset();
});

afterEach(() => {
  cleanup();
  window.history.replaceState(null, '', '/');
});

describe('OneTimeCodeLogin return navigation', () => {
  it('returns to the protected route that initiated login', async () => {
    renderLogin('/app/customers/customer-1');

    await completeLogin();

    expect(await screen.findByRole('heading', { name: 'Kunde' })).toBeInTheDocument();
    expect(mocks.login).toHaveBeenCalledWith('user@example.com', '123456');
    expect(mocks.clearReauthInFlight).toHaveBeenCalledOnce();
  });

  it('falls back to app home for an unsafe return target', async () => {
    renderLogin('//example.com/phishing');

    await completeLogin();

    expect(await screen.findByRole('heading', { name: 'App' })).toBeInTheDocument();
  });
});
