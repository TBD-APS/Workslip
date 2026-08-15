import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Bell,
  BellRing,
  CheckCheck,
  ChevronDown,
  CircleCheck,
  ClipboardCheck,
  Info,
  UserPlus,
  X,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiClient } from '../../lib/axios';
import { notificationListQueryKey } from '../../lib/notificationQueryKeys';
import { useAuth } from '../../providers/useAuth';
import { formatRelativeActivityTime } from './activityFeed';
import { Drawer } from './Drawer';
import './ActivityFeed.css';
import './NotificationsDrawer.css';

type NotificationItem = {
  id: string;
  title: string;
  body: string;
  url?: string | null;
  createdUtc: string;
  isRead: boolean;
  status?: string;
};

type NotificationGroup = {
  key: string;
  url: string | null;
  items: NotificationItem[];
  latest: NotificationItem;
  unreadCount: number;
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
    return <CircleCheck size={17} />;
  }

  if (haystack.includes('tildelt') || haystack.includes('medarbejder') || haystack.includes('assigned')) {
    return <UserPlus size={17} />;
  }

  if (haystack.includes('opgave') || haystack.includes('sag') || haystack.includes('job')) {
    return <ClipboardCheck size={17} />;
  }

  return item.isRead ? <Info size={17} /> : <BellRing size={17} />;
};

const getNotificationAvatarTone = (item: NotificationItem) => {
  const haystack = `${item.title} ${item.body}`.toLocaleLowerCase('da-DK');

  if (haystack.includes('afvist') || haystack.includes('fejl')) return 'activity-avatar-danger';
  if (haystack.includes('færdig') || haystack.includes('completed') || haystack.includes('godkend')) {
    return 'activity-avatar-success';
  }
  if (haystack.includes('advar') || haystack.includes('mangler')) return 'activity-avatar-warning';
  return 'activity-avatar-primary';
};

const getNotificationGroupKey = (item: NotificationItem) =>
  item.url ? `resource:${item.url}` : `event:${item.id}`;

const groupNotifications = (items: NotificationItem[]): NotificationGroup[] => {
  const groups = new Map<string, NotificationGroup>();

  for (const item of items) {
    const key = getNotificationGroupKey(item);
    const existing = groups.get(key);

    if (existing) {
      existing.items.push(item);
      existing.unreadCount += item.isRead ? 0 : 1;
      continue;
    }

    groups.set(key, {
      key,
      url: item.url ?? null,
      items: [item],
      latest: item,
      unreadCount: item.isRead ? 0 : 1,
    });
  }

  return [...groups.values()];
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
  const [expandedGroupKeys, setExpandedGroupKeys] = useState<Set<string>>(() => new Set());
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
  const filteredItems = useMemo(
    () => (filter === 'unread' ? items.filter((item) => !item.isRead) : items),
    [filter, items],
  );
  const visibleGroups = useMemo(() => groupNotifications(filteredItems), [filteredItems]);
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

  const markItemsRead = async (itemsToRead: NotificationItem[]) => {
    const unreadItems = itemsToRead.filter((item) => !item.isRead);
    if (unreadItems.length === 0) return true;

    try {
      await Promise.all(unreadItems.map((item) => apiClient.patch(
        `/api/notifications/${item.id}/read`,
        undefined,
        { skipGlobalErrorToast: true },
      )));
      const ids = new Set(unreadItems.map((item) => item.id));
      updateItems((current) => current.map((entry) =>
        ids.has(entry.id) ? { ...entry, isRead: true } : entry));
      setActionError(null);
      return true;
    } catch {
      setActionError('Notifikationen kunne ikke markeres som læst.');
      void refetch();
      return false;
    }
  };

  const openNotifications = async (itemsToOpen: NotificationItem[], url?: string | null) => {
    const didMarkRead = await markItemsRead(itemsToOpen);
    if (!didMarkRead) return;

    if (url) {
      closeDrawer();
      navigate(url);
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

  const toggleGroup = (key: string) => {
    setExpandedGroupKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
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
      <div className="notifications-overview" aria-live="polite">
        <div>
          <strong>{unreadCount === 0 ? 'Alt er set' : `${unreadCount} ulæst${unreadCount === 1 ? '' : 'e'}`}</strong>
          <span>Aktivitet, der vedrører dig, samlet ét sted.</span>
        </div>
        {unreadCount > 0 && (
          <button type="button" onClick={() => void markAllRead()}>
            <CheckCheck size={16} />
            Marker alle læst
          </button>
        )}
      </div>

      <div className="notifications-tabs" role="tablist" aria-label="Filtrer notifikationer">
        <button
          type="button"
          role="tab"
          aria-selected={filter === 'all'}
          className={filter === 'all' ? 'active' : ''}
          onClick={() => setFilter('all')}
        >
          Al aktivitet
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

      {!loading && filteredItems.length > 0 && (
        <div className="notifications-toolbar">
          <span>{filter === 'unread' ? 'Kræver din opmærksomhed' : 'Seneste aktivitet'}</span>
          <span>{visibleGroups.length} {visibleGroups.length === 1 ? 'sag/emne' : 'sager/emner'}</span>
        </div>
      )}

      {loading ? (
        <div className="notifications-skeleton" aria-label="Henter notifikationer">
          <span />
          <span />
          <span />
        </div>
      ) : visibleGroups.length > 0 ? (
        <div className="notifications-list activity-feed">
          <section className="activity-section" aria-label="Seneste aktivitet">
            {visibleGroups.map((group) => {
              const { latest } = group;
              const isExpanded = expandedGroupKeys.has(group.key);
              const isGrouped = group.items.length > 1;
              const isDeletingSingle = group.items.length === 1 && deletingIds.has(latest.id);
              const bodyLines = getBodyLines(latest.body);
              const rowLabel = `${latest.title}${isGrouped ? `, ${group.items.length} hændelser` : ''}${group.unreadCount > 0 ? `, ${group.unreadCount} ulæst${group.unreadCount === 1 ? '' : 'e'}` : ''}`;

              return (
                <div
                  key={group.key}
                  className={`activity-row notification-item${group.unreadCount > 0 ? ' activity-row-unread notification-item-unread' : ''}${isDeletingSingle ? ' notification-item-deleting' : ''}`}
                >
                  <span className={`activity-avatar ${getNotificationAvatarTone(latest)}`} aria-hidden="true">
                    {getNotificationIcon(latest)}
                  </span>

                  <div className="activity-content">
                    <button
                      type="button"
                      className="activity-primary-action notification-activity-main"
                      onClick={() => void openNotifications(group.items, group.url)}
                      aria-label={rowLabel}
                      disabled={isDeletingSingle}
                    >
                      <span className="activity-heading">
                        <strong className="activity-title">{latest.title}</strong>
                        <time
                          className="activity-time"
                          dateTime={latest.createdUtc}
                          title={new Date(latest.createdUtc).toLocaleString('da-DK')}
                        >
                          {formatRelativeActivityTime(latest.createdUtc)}
                        </time>
                      </span>
                      {bodyLines.length > 0 && (
                        <span className="notification-activity-body">
                          {bodyLines.map((line, lineIndex) => (
                            <span key={`${latest.id}-${lineIndex}`} className="activity-body">
                              {line}
                            </span>
                          ))}
                        </span>
                      )}
                    </button>

                    <div className="activity-actions">
                      {isGrouped && (
                        <button
                          type="button"
                          className="activity-action notification-group-toggle"
                          onClick={() => toggleGroup(group.key)}
                          aria-expanded={isExpanded}
                          aria-label={`${isExpanded ? 'Skjul' : 'Vis'} ${group.items.length} hændelser for ${latest.title}`}
                        >
                          <ChevronDown className={isExpanded ? 'notification-chevron-expanded' : ''} size={15} />
                          {group.items.length} hændelser
                        </button>
                      )}
                      {group.unreadCount > 0 && (
                        <span className="activity-badge">
                          {group.unreadCount} ulæst{group.unreadCount === 1 ? '' : 'e'}
                        </span>
                      )}
                      {!isGrouped && (
                        <button
                          type="button"
                          className="activity-action notification-delete-inline"
                          onClick={() => void deleteNotification(latest)}
                          disabled={isDeletingSingle}
                          aria-label={`Slet ${latest.title}`}
                        >
                          <X size={14} />
                          Slet
                        </button>
                      )}
                    </div>
                  </div>

                  {isGrouped && isExpanded && (
                    <div className="activity-details notification-group-details">
                      <div className="activity-sublist">
                        {group.items.map((item) => {
                          const isDeleting = deletingIds.has(item.id);
                          return (
                            <div key={item.id} className="activity-subrow">
                              <button
                                type="button"
                                className="activity-primary-action activity-subrow-main"
                                onClick={() => void openNotifications([item], item.url)}
                                disabled={isDeleting}
                                aria-label={`${item.title}${item.isRead ? '' : ', ulæst'}`}
                              >
                                <span className="activity-subrow-title">{item.title}</span>
                                <span className="activity-subrow-body">{getBodyLines(item.body).join(' · ')}</span>
                                <time className="activity-time" dateTime={item.createdUtc}>
                                  {formatRelativeActivityTime(item.createdUtc)}
                                </time>
                              </button>
                              <button
                                type="button"
                                className="activity-action notification-delete-inline"
                                onClick={() => void deleteNotification(item)}
                                disabled={isDeleting}
                                aria-label={`Slet ${item.title}`}
                              >
                                <X size={14} />
                              </button>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </section>
        </div>
      ) : !error ? (
        <div className="notifications-empty">
          <span className="notifications-empty-icon" aria-hidden="true">
            {filter === 'unread' ? <CheckCheck size={30} /> : <Bell size={30} />}
          </span>
          <strong>{filter === 'unread' ? 'Du er helt ajour' : 'Ingen aktivitet endnu'}</strong>
          <span>{filter === 'unread' ? 'Der er ikke noget, der kræver din opmærksomhed lige nu.' : 'Nye hændelser og opgaver dukker op her.'}</span>
        </div>
      ) : null}
    </Drawer>
  );
}
