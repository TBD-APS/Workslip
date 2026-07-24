import { Bell } from 'lucide-react';
import { Drawer } from './Drawer';
import { useEffect, useState } from 'react';
import { apiClient } from '../../lib/axios';
import { useNavigate } from 'react-router-dom';

type NotificationItem = { id: string; title: string; body: string; url?: string | null; createdUtc: string; isRead: boolean };

type NotificationsDrawerProps = {
  isOpen: boolean;
  onClose: () => void;
};

export function NotificationsDrawer({ isOpen, onClose }: NotificationsDrawerProps) {
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    if (!isOpen) return;
    setLoading(true);
    void apiClient.get('/api/notifications', { params: { limit: 50 } })
      .then((response) => setItems(Array.isArray(response) ? response as NotificationItem[] : []))
      .catch(() => setItems([]))
      .finally(() => setLoading(false));
  }, [isOpen]);

  const markRead = async (item: NotificationItem) => {
    if (!item.isRead) {
      await apiClient.patch(`/api/notifications/${item.id}/read`);
      setItems((current) => current.map((entry) => entry.id === item.id ? { ...entry, isRead: true } : entry));
    }
    if (item.url) { onClose(); navigate(item.url); }
  };

  return (
    <Drawer
      isOpen={isOpen}
      onClose={onClose}
      title="Notifikationer"
      icon={<Bell size={20} />}
    >
      {loading ? <div className="drawer-empty">Henter notifikationer…</div> : items.length === 0 ? <div className="drawer-empty">Ingen notifikationer endnu.</div> : (
        <div className="notifications-list">
          {items.map((item) => (
            <button key={item.id} type="button" className={`notification-item${item.isRead ? '' : ' notification-item-unread'}`} onClick={() => void markRead(item)}>
              <strong>{item.title}</strong>
              <span>{item.body}</span>
              <small>{new Date(item.createdUtc).toLocaleString('da-DK')}</small>
            </button>
          ))}
        </div>
      )}
    </Drawer>
  );
}
