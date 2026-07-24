import { Bell, CheckCheck } from 'lucide-react';
import { Drawer } from './Drawer';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { apiClient } from '../../lib/axios';
import { useNavigate } from 'react-router-dom';
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

const countUnread = (items: NotificationItem[]) =>
  items.reduce((count, item) => count + (item.isRead ? 0 : 1), 0);

const getBodyLines = (body: string) =>
  body
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

export function NotificationsDrawer({
  isOpen,
  onClose,
  onUnreadCountChange,
}: NotificationsDrawerProps) {
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  const replaceItems = useCallback((nextItems: NotificationItem[]) => {
    setItems(nextItems);
    onUnreadCountChange?.(countUnread(nextItems));
  }, [onUnreadCountChange]);

  const updateItems = useCallback((updater: (current: NotificationItem[]) => NotificationItem[]) => {
    setItems((current) => {
      const nextItems = updater(current);
      onUnreadCountChange?.(countUnread(nextItems));
      return nextItems;
    });
  }, [onUnreadCountChange]);

  const loadNotifications = useCallback(async (showLoading: boolean) => {
    if (showLoading) setLoading(true);
    setError(null);

    try {
      const response = await apiClient.get('/api/notifications', {
        params: { limit: 50 },
        skipGlobalErrorToast: true,
      });
      replaceItems(Array.isArray(response) ? response as NotificationItem[] : []);
    } catch {
      setError('Notifikationerne kunne ikke hentes. Prøv igen.');
    } finally {
      if (showLoading) setLoading(false);
    }
  }, [replaceItems]);

  useEffect(() => {
    void loadNotifications(isOpen);
  }, [isOpen, loadNotifications]);

  const unreadCount = useMemo(() => countUnread(items), [items]);

  const markRead = async (item: NotificationItem) => {
    if (!item.isRead) {
      try {
        await apiClient.patch(`/api/notifications/${item.id}/read`, undefined, {
          skipGlobalErrorToast: true,
        });
        updateItems((current) => current.map((entry) =>
          entry.id === item.id ? { ...entry, isRead: true } : entry));
        setError(null);
      } catch {
        setError('Notifikationen kunne ikke markeres som læst.');
      }
    }

    if (item.url) {
      onClose();
      navigate(item.url);
    }
  };

  const markAllRead = async () => {
    try {
      await apiClient.post('/api/notifications/read-all', undefined, {
        skipGlobalErrorToast: true,
      });
      updateItems((current) => current.map((item) => ({ ...item, isRead: true })));
      setError(null);
    } catch {
      setError('Notifikationerne kunne ikke markeres som læst.');
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
      onClose={onClose}
      title="Notifikationer"
      icon={<Bell size={20} />}
    >
      {error && (
        <div className="notification-error" role="alert">
          <span>{error}</span>
          <button type="button" onClick={() => void loadNotifications(true)}>
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
          {items.map((item) => (
            <button
              key={item.id}
              type="button"
              className={`notification-item${item.isRead ? '' : ' notification-item-unread'}`}
              onClick={() => void markRead(item)}
              aria-label={`${item.title}${item.isRead ? '' : ', ulæst'}`}
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
          ))}
        </div>
      ) : !error ? (
        <div className="drawer-empty">Ingen notifikationer endnu.</div>
      ) : null}
    </Drawer>
  );
}
