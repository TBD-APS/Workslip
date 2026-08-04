import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../lib/axios';
import {
  notificationListQueryKey,
  NOTIFICATION_QUERY_PREFIX,
} from '../../lib/notificationQueryKeys';
import { NotificationsDrawer } from './NotificationsDrawer';

vi.mock('../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    patch: vi.fn(),
    post: vi.fn(),
    delete: vi.fn(),
  },
}));

vi.mock('../../providers/useAuth', () => ({
  useAuth: () => ({
    user: {
      id: 'user-1',
      organizationId: 'organization-1',
    },
  }),
}));

vi.mock('./Drawer', () => ({
  Drawer: ({
    children,
    onClose,
  }: {
    children: ReactNode;
    onClose: () => void;
  }) => (
    <div>
      <button type="button" aria-label="Luk testdrawer" onClick={onClose} />
      {children}
    </div>
  ),
}));

const readNotification = {
  id: 'notification-1',
  title: 'Eksisterende',
  body: 'Allerede læst',
  url: null,
  createdUtc: '2026-08-04T05:00:00.000Z',
  isRead: true,
};

const unreadNotification = {
  id: 'notification-2',
  title: 'Ny sag',
  body: 'Du har fået en ny sag',
  url: '/app/job/job-2',
  createdUtc: '2026-08-04T05:01:00.000Z',
  isRead: false,
};

function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        staleTime: Number.POSITIVE_INFINITY,
      },
    },
  });
}

describe('NotificationsDrawer', () => {
  beforeEach(() => vi.clearAllMocks());

  it('updates the bell count when the notification query is invalidated while closed', async () => {
    vi.mocked(apiClient.get)
      .mockResolvedValueOnce([readNotification])
      .mockResolvedValueOnce([readNotification, unreadNotification]);

    const queryClient = createTestQueryClient();
    const onUnreadCountChange = vi.fn();

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <NotificationsDrawer
            isOpen={false}
            onClose={vi.fn()}
            onUnreadCountChange={onUnreadCountChange}
          />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await waitFor(() => expect(apiClient.get).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(onUnreadCountChange).toHaveBeenLastCalledWith(0));

    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: NOTIFICATION_QUERY_PREFIX });
    });

    await waitFor(() => expect(apiClient.get).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(onUnreadCountChange).toHaveBeenLastCalledWith(1));
    expect(queryClient.getQueryData(notificationListQueryKey(
      'user-1',
      'organization-1',
    ))).toEqual([
      readNotification,
      unreadNotification,
    ]);
  });

  it('clears a stale action error when the drawer closes', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([
      { ...unreadNotification, url: null },
    ]);
    vi.mocked(apiClient.patch).mockRejectedValue(new Error('request failed'));
    const onClose = vi.fn();

    render(
      <QueryClientProvider client={createTestQueryClient()}>
        <MemoryRouter>
          <NotificationsDrawer
            isOpen
            onClose={onClose}
          />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Ny sag, ulæst' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Notifikationen kunne ikke markeres som læst.',
    );

    fireEvent.click(screen.getByRole('button', { name: 'Luk testdrawer' }));

    expect(onClose).toHaveBeenCalledOnce();
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });
});
