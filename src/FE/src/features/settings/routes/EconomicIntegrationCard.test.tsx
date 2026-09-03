import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { EconomicIntegrationCard } from './EconomicIntegrationCard';

const {
  connectionHook,
  connectMutation,
  disconnectMutation,
  invalidateQueries,
  notifySuccess,
  notifyError,
} = vi.hoisted(() => ({
  connectionHook: vi.fn(),
  connectMutation: vi.fn(),
  disconnectMutation: vi.fn(),
  invalidateQueries: vi.fn(),
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
}));

vi.mock('@tanstack/react-query', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-query')>('@tanstack/react-query');
  return {
    ...actual,
    useQueryClient: () => ({ invalidateQueries }),
  };
});

vi.mock('../api', () => ({
  useEconomicConnection: () => connectionHook(),
  useStartEconomicConnection: () => ({
    mutateAsync: connectMutation,
    isPending: false,
  }),
  useDisconnectEconomic: () => ({
    mutateAsync: disconnectMutation,
    isPending: false,
  }),
}));

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: notifySuccess,
    error: notifyError,
  },
}));

const disconnected = {
  isLoading: false,
  isError: false,
  data: {
    available: true,
    connected: false,
    providerId: 'economics',
    providerDisplayName: 'e-conomic',
    agreementNumber: null,
    companyName: null,
    connectedAt: null,
  },
};

beforeEach(() => {
  connectionHook.mockReturnValue(disconnected);
});

afterEach(() => {
  cleanup();
  connectionHook.mockReset();
  connectMutation.mockReset();
  disconnectMutation.mockReset();
  invalidateQueries.mockReset();
  notifySuccess.mockReset();
  notifyError.mockReset();
});

describe('EconomicIntegrationCard', () => {
  it('shows a single simple connect action when e-conomic is available', () => {
    render(
      <MemoryRouter>
        <EconomicIntegrationCard />
      </MemoryRouter>,
    );

    expect(screen.getByRole('button', { name: 'Forbind e-conomic' })).toBeEnabled();
    expect(screen.getByText(/Du skal ikke kopiere tokens/i)).toBeInTheDocument();
    expect(screen.queryByText('Forbundet')).not.toBeInTheDocument();
  });

  it('shows company and agreement metadata after connection', () => {
    connectionHook.mockReturnValue({
      isLoading: false,
      isError: false,
      data: {
        available: true,
        connected: true,
        providerId: 'economics',
        providerDisplayName: 'e-conomic',
        agreementNumber: '123456',
        companyName: 'Niels VVS',
        connectedAt: '2026-09-03T20:00:00Z',
      },
    });

    render(
      <MemoryRouter>
        <EconomicIntegrationCard />
      </MemoryRouter>,
    );

    expect(screen.getByText('Forbundet')).toBeInTheDocument();
    expect(screen.getByText('Niels VVS')).toBeInTheDocument();
    expect(screen.getByText('123456')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Afbryd forbindelse' })).toBeEnabled();
  });

  it('turns the callback query into success feedback and refreshes status', async () => {
    render(
      <MemoryRouter initialEntries={['/app/settings?economic=connected']}>
        <EconomicIntegrationCard />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(notifySuccess).toHaveBeenCalledWith('e-conomic er forbundet');
    });
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['/api/accounting/economic/connection'] });
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['/api/accounting/status'] });
    expect(notifyError).not.toHaveBeenCalled();
  });
});
