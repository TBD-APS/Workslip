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

function renderDrawer(props: { isOpen?: boolean; onClose?: () => void } = {}) {
  const onClose = props.onClose ?? vi.fn();
  const queryClient = createTestQueryClient();

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NotificationsDrawer
          isOpen={props.isOpen ?? true}
          onClose={onClose}
        />
      </MemoryRouter>
    </QueryClientProvider>,
  );

  return { onClose, queryClient };
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

  it('filters the inbox to unread notifications and shows the all-clear state after marking all read', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([readNotification, { ...unreadNotification, url: null }]);
    vi.mocked(apiClient.post).mockResolvedValue(undefined);

    renderDrawer();

    expect(await screen.findByRole('button', { name: 'Ny sag, 1 ulæst' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eksisterende' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: /Ulæste/ }));

    expect(screen.getByRole('button', { name: 'Ny sag, 1 ulæst' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Eksisterende' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Marker alle læst/ }));

    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith(
      '/api/notifications/read-all',
      undefined,
      { skipGlobalErrorToast: true },
    ));
    expect(await screen.findByText('Du er helt ajour')).toBeInTheDocument();
    expect(screen.getByText('Der er ikke noget, der kræver din opmærksomhed lige nu.')).toBeInTheDocument();
  });

  it('groups repeated activity only when notifications point to the same resource', async () => {
    const newerNotification = {
      ...unreadNotification,
      id: 'notification-3',
      title: 'Sag opdateret',
      body: 'Status er ændret',
      createdUtc: '2026-08-04T05:02:00.000Z',
    };
    vi.mocked(apiClient.get).mockResolvedValue([newerNotification, unreadNotification]);

    renderDrawer();

    expect(await screen.findByRole('button', {
      name: 'Sag opdateret, 2 hændelser, 2 ulæste',
    })).toBeInTheDocument();

    const toggle = screen.getByRole('button', {
      name: 'Vis 2 hændelser for Sag opdateret',
    });
    fireEvent.click(toggle);

    expect(screen.getByRole('button', { name: 'Sag opdateret, ulæst' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ny sag, ulæst' })).toBeInTheDocument();
  });

  it('does not group unrelated notifications without a resource link', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([
      { ...unreadNotification, id: 'notification-4', url: null, title: 'Opgave opdateret' },
      { ...unreadNotification, id: 'notification-5', url: null, title: 'Opgave opdateret' },
    ]);

    renderDrawer();

    expect(await screen.findAllByRole('button', { name: 'Opgave opdateret, 1 ulæst' })).toHaveLength(2);
    expect(screen.queryByRole('button', { name: /Vis 2 hændelser/ })).not.toBeInTheDocument();
  });

  it('marks every unread event in a grouped resource before following the deep link', async () => {
    const newerNotification = {
      ...unreadNotification,
      id: 'notification-3',
      title: 'Sag opdateret',
      body: 'Status er ændret',
      createdUtc: '2026-08-04T05:02:00.000Z',
    };
    vi.mocked(apiClient.get).mockResolvedValue([newerNotification, unreadNotification]);
    vi.mocked(apiClient.patch).mockResolvedValue(undefined);
    const onClose = vi.fn();

    renderDrawer({ onClose });

    fireEvent.click(await screen.findByRole('button', {
      name: 'Sag opdateret, 2 hændelser, 2 ulæste',
    }));

    await waitFor(() => expect(apiClient.patch).toHaveBeenCalledTimes(2));
    expect(apiClient.patch).toHaveBeenCalledWith(
      '/api/notifications/notification-3/read',
      undefined,
      { skipGlobalErrorToast: true },
    );
    expect(apiClient.patch).toHaveBeenCalledWith(
      '/api/notifications/notification-2/read',
      undefined,
      { skipGlobalErrorToast: true },
    );
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
  });

  it('clears a stale action error when the drawer closes', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([
      { ...unreadNotification, url: null },
    ]);
    vi.mocked(apiClient.patch).mockRejectedValue(new Error('request failed'));
    const onClose = vi.fn();

    renderDrawer({ onClose });

    fireEvent.click(await screen.findByRole('button', { name: 'Ny sag, 1 ulæst' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Notifikationen kunne ikke markeres som læst.',
    );

    fireEvent.click(screen.getByRole('button', { name: 'Luk testdrawer' }));

    expect(onClose).toHaveBeenCalledOnce();
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });
});
