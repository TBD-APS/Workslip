import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Bell,
  BellRing,
  CheckCheck,
  CircleCheck,
  ClipboardCheck,
  Info,
  Sparkles,
  UserPlus,
  X,
} from 'lucide-react';
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

type NotificationFilter = 'all' | 'unread';

const EMPTY_NOTIFICATIONS: NotificationItem[] = [];

const countUnread = (items: NotificationItem[]) =>
  items.reduce((count, item) => count + (item.isRead ? 0 : 1), 0);

const getBodyLines = (body: string) =>
  body
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

const getNotificationIcon = (item: NotificationItem) => {
  const haystack = `${item.title} ${item.body}`.toLocaleLowerCase('da-DK');

  if (haystack.includes('færdig') || haystack.includes('completed') || haystack.includes('godkend')) {
    return <CircleCheck size={18} />;
  }

  if (haystack.includes('tildelt') || haystack.includes('medarbejder') || haystack.includes('assigned')) {
    return <UserPlus size={18} />;
  }

  if (haystack.includes('opgave') || haystack.includes('sag') || haystack.includes('job')) {
    return <ClipboardCheck size={18} />;
  }

  return item.isRead ? <Info size={18} /> : <BellRing size={18} />;
};

const formatRelativeCreatedAt = (createdUtc: string) => {
  const createdAt = new Date(createdUtc);
  const diffMs = Date.now() - createdAt.getTime();
  const diffMinutes = Math.max(0, Math.round(diffMs / 60_000));

  if (diffMinutes < 1) return 'Nu';
  if (diffMinutes < 60) return `${diffMinutes} min. siden`;

  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours} t. siden`;

  const diffDays = Math.round(diffHours / 24);
  if (diffDays < 7) return diffDays === 1 ? 'I går' : `${diffDays} dage siden`;

  return createdAt.toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'short',
  });
};

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
  const [filter, setFilter] = useState<NotificationFilter>('all');
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
  const visibleItems = useMemo(
    () => (filter === 'unread' ? items.filter((item) => !item.isRead) : items),
    [filter, items],
  );
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
        return;
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

  return (
    <Drawer
      isOpen={isOpen}
      onClose={closeDrawer}
      title="Indbakke"
      ariaLabel="Notifikationsindbakke"
      icon={<Bell size={20} />}
      className="history-drawer notifications-drawer"
    >
      <div className="notifications-hero">
        <span className="notifications-hero-icon" aria-hidden="true">
          <Sparkles size={18} />
        </span>
        <div>
          <strong>Hold styr på det vigtigste</strong>
          <span>{unreadCount === 0 ? 'Du er helt ajour.' : `${unreadCount} ${unreadCount === 1 ? 'ting' : 'ting'} kræver din opmærksomhed.`}</span>
        </div>
      </div>

      <div className="notifications-tabs" role="tablist" aria-label="Filtrer notifikationer">
        <button
          type="button"
          role="tab"
          aria-selected={filter === 'all'}
          className={filter === 'all' ? 'active' : ''}
          onClick={() => setFilter('all')}
        >
          Alle
          <span>{items.length}</span>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={filter === 'unread'}
          className={filter === 'unread' ? 'active' : ''}
          onClick={() => setFilter('unread')}
        >
          Ulæste
          <span>{unreadCount}</span>
        </button>
      </div>

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
          <span>{filter === 'unread' ? 'Kræver opmærksomhed' : 'Seneste aktivitet'}</span>
          {unreadCount > 0 && (
            <button type="button" onClick={() => void markAllRead()}>
              <CheckCheck size={16} />
              Marker alle som læst
            </button>
          )}
        </div>
      )}

      {loading ? (
        <div className="notifications-skeleton" aria-label="Henter notifikationer">
          <span />
          <span />
          <span />
        </div>
      ) : visibleItems.length > 0 ? (
        <div className="notifications-list">
          {visibleItems.map((item, index) => {
            const isDeleting = deletingIds.has(item.id);
            return (
              <div
                key={item.id}
                className={`notification-item${item.isRead ? '' : ' notification-item-unread'}${isDeleting ? ' notification-item-deleting' : ''}`}
                style={{ '--notification-index': index } as React.CSSProperties}
              >
                {!item.isRead && <span className="notification-unread-dot" aria-hidden="true" />}
                <span className="notification-type-icon" aria-hidden="true">
                  {getNotificationIcon(item)}
                </span>
                <button
                  type="button"
                  className="notification-item-main"
                  onClick={() => void markRead(item)}
                  aria-label={`${item.title}${item.isRead ? '' : ', ulæst'}`}
                  disabled={isDeleting}
                >
                  <span className="notification-item-header">
                    <strong className="notification-item-title">{item.title}</strong>
                    <small title={new Date(item.createdUtc).toLocaleString('da-DK')}>
                      {formatRelativeCreatedAt(item.createdUtc)}
                    </small>
                  </span>
                  <span className="notification-body">
                    {getBodyLines(item.body).map((line, lineIndex) => (
                      <span key={`${item.id}-${lineIndex}`} className="notification-body-line">
                        {line}
                      </span>
                    ))}
                  </span>
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
        <div className="notifications-empty">
          <span className="notifications-empty-icon" aria-hidden="true">
            {filter === 'unread' ? <CheckCheck size={30} /> : <Bell size={30} />}
          </span>
          <strong>{filter === 'unread' ? 'Du er helt ajour' : 'Ingen notifikationer endnu'}</strong>
          <span>{filter === 'unread' ? 'Der er ikke noget, der kræver din opmærksomhed lige nu.' : 'Nye hændelser og opgaver dukker op her.'}</span>
        </div>
      ) : null}
    </Drawer>
  );
}
