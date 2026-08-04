import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, waitFor } from '@testing-library/react';
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
    user: { id: 'user-1' },
  }),
}));

vi.mock('./Drawer', () => ({
  Drawer: ({ children }: { children: ReactNode }) => <div>{children}</div>,
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

describe('NotificationsDrawer', () => {
  beforeEach(() => vi.clearAllMocks());

  it('updates the bell count when the notification query is invalidated while closed', async () => {
    vi.mocked(apiClient.get)
      .mockResolvedValueOnce([readNotification])
      .mockResolvedValueOnce([readNotification, unreadNotification]);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
          staleTime: Number.POSITIVE_INFINITY,
        },
      },
    });
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
    expect(queryClient.getQueryData(notificationListQueryKey('user-1'))).toEqual([
      readNotification,
      unreadNotification,
    ]);
  });
});
