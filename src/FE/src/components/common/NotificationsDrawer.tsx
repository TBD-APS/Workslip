import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Bell, CheckCheck, X } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiClient } from '../../lib/axios';
import { notificationListQueryKey } from '../../lib/notificationQueryKeys';
import { useAuth } from '../../providers/useAuth';
import { Drawer } from './Drawer';
import './NotificationsDrawer.css';

type NotificationItem = {
  id: string;
  title: string;
  body: string;
  url?: string | null;
  createdUtc: string;
  isRead: boolean;
};

type NotificationsDrawerProps = {
  isOpen: boolean;
  onClose: () => void;
  onUnreadCountChange?: (count: number) => void;
};

const EMPTY_NOTIFICATIONS: NotificationItem[] = [];

const countUnread = (items: NotificationItem[]) =>
  items.reduce((count, item) => count + (item.isRead ? 0 : 1), 0);

const getBodyLines = (body: string) =>
  body
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

async function getNotifications(): Promise<NotificationItem[]> {
  const response = await apiClient.get('/api/notifications', {
    params: { limit: 50 },
    skipGlobalErrorToast: true,
  });

  return Array.isArray(response) ? response as NotificationItem[] : [];
}

export function NotificationsDrawer({
  isOpen,
  onClose,
  onUnreadCountChange,
}: NotificationsDrawerProps) {
  const { user } = useAuth();
  const userId = user?.id ?? '';
  const organizationId = user?.organizationId ?? '';
  const queryKey = useMemo(
    () => notificationListQueryKey(userId, organizationId),
    [organizationId, userId],
  );
  const queryClient = useQueryClient();
  const {
    data: items = EMPTY_NOTIFICATIONS,
    isError,
    isPending,
    refetch,
  } = useQuery({
    queryKey,
    queryFn: getNotifications,
    enabled: userId.length > 0 && organizationId.length > 0,
  });
  const [actionError, setActionError] = useState<string | null>(null);
  const [deletingIds, setDeletingIds] = useState<Set<string>>(() => new Set());
  const navigate = useNavigate();

  const updateItems = useCallback((updater: (current: NotificationItem[]) => NotificationItem[]) => {
    queryClient.setQueryData<NotificationItem[]>(queryKey, (current) => updater(current ?? []));
  }, [queryClient, queryKey]);

  const closeDrawer = useCallback(() => {
    setActionError(null);
    onClose();
  }, [onClose]);

  useEffect(() => {
    onUnreadCountChange?.(countUnread(items));
  }, [items, onUnreadCountChange]);

  useEffect(() => {
    if (isOpen && userId && organizationId) {
      void refetch();
    }
  }, [isOpen, organizationId, refetch, userId]);

  const unreadCount = useMemo(() => countUnread(items), [items]);
  const loading = userId.length > 0
    && organizationId.length > 0
    && isPending
    && items.length === 0;
  const error = actionError
    ?? (isError ? 'Notifikationerne kunne ikke hentes. Prøv igen.' : null);

  const retryLoad = async () => {
    setActionError(null);
    await refetch();
  };

  const markRead = async (item: NotificationItem) => {
    if (!item.isRead) {
      try {
        await apiClient.patch(`/api/notifications/${item.id}/read`, undefined, {
          skipGlobalErrorToast: true,
        });
        updateItems((current) => current.map((entry) =>
          entry.id === item.id ? { ...entry, isRead: true } : entry));
        setActionError(null);
      } catch {
        setActionError('Notifikationen kunne ikke markeres som læst.');
      }
    }

    if (item.url) {
      closeDrawer();
      navigate(item.url);
    }
  };

  const markAllRead = async () => {
    try {
      await apiClient.post('/api/notifications/read-all', undefined, {
        skipGlobalErrorToast: true,
      });
      updateItems((current) => current.map((item) => ({ ...item, isRead: true })));
      setActionError(null);
    } catch {
      setActionError('Notifikationerne kunne ikke markeres som læst.');
    }
  };

  const deleteNotification = async (item: NotificationItem) => {
    setDeletingIds((current) => new Set(current).add(item.id));
    try {
      await apiClient.delete(`/api/notifications/${item.id}`, {
        skipGlobalErrorToast: true,
      });
      updateItems((current) => current.filter((entry) => entry.id !== item.id));
      setActionError(null);
    } catch {
      setActionError('Notifikationen kunne ikke slettes. Prøv igen.');
    } finally {
      setDeletingIds((current) => {
        const next = new Set(current);
        next.delete(item.id);
        return next;
      });
    }
  };

  const formatCreatedAt = (createdUtc: string) =>
    new Date(createdUtc).toLocaleString('da-DK', {
      dateStyle: 'medium',
      timeStyle: 'short',
    });

  return (
    <Drawer
      isOpen={isOpen}
      onClose={closeDrawer}
      title="Notifikationer"
      ariaLabel="Notifikationer"
      icon={<Bell size={20} />}
      className="history-drawer notifications-drawer"
    >
      {error && (
        <div className="notification-error" role="alert">
          <span>{error}</span>
          <button type="button" onClick={() => void retryLoad()}>
            Prøv igen
          </button>
        </div>
      )}

      {!loading && items.length > 0 && (
        <div className="notifications-toolbar">
          <span>{unreadCount === 0 ? 'Ingen ulæste' : `${unreadCount} ulæste`}</span>
          {unreadCount > 0 && (
            <button type="button" onClick={() => void markAllRead()}>
              <CheckCheck size={16} />
              Marker alle som læst
            </button>
          )}
        </div>
      )}

      {loading ? (
        <div className="drawer-empty">Henter notifikationer…</div>
      ) : items.length > 0 ? (
        <div className="notifications-list">
          {items.map((item) => {
            const isDeleting = deletingIds.has(item.id);
            return (
              <div
                key={item.id}
                className={`notification-item${item.isRead ? '' : ' notification-item-unread'}${isDeleting ? ' notification-item-deleting' : ''}`}
              >
                <button
                  type="button"
                  className="notification-item-main"
                  onClick={() => void markRead(item)}
                  aria-label={`${item.title}${item.isRead ? '' : ', ulæst'}`}
                  disabled={isDeleting}
                >
                  <span className="notification-item-header">
                    <strong className="notification-item-title">{item.title}</strong>
                    {!item.isRead && <span className="notification-new-label">Ny</span>}
                  </span>
                  <span className="notification-body">
                    {getBodyLines(item.body).map((line, index) => (
                      <span key={`${item.id}-${index}`} className="notification-body-line">
                        {line}
                      </span>
                    ))}
                  </span>
                  <small>{formatCreatedAt(item.createdUtc)}</small>
                </button>
                <button
                  type="button"
                  className="notification-delete"
                  onClick={() => void deleteNotification(item)}
                  disabled={isDeleting}
                  aria-label={`Slet ${item.title}`}
                  title="Slet notifikation"
                >
                  <X size={16} />
                </button>
              </div>
            );
          })}
        </div>
      ) : !error ? (
        <div className="drawer-empty">Ingen notifikationer endnu.</div>
      ) : null}
    </Drawer>
  );
}
