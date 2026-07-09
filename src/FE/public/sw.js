/* eslint-disable no-restricted-globals */

self.addEventListener('push', (event) => {
  if (!event.data) {
    return;
  }

  let payload;
  try {
    payload = event.data.json();
  } catch (e) {
    console.error('Failed to parse push payload:', e);
    return;
  }

  // The backend sends the notification payload in a specific format:
  // { title, options: { body, icon, badge, tag, data: { url } } }
  
  const title = payload.title || 'Workslip';
  const options = payload.options || {};
  const body = options.body || '';
  const icon = options.icon || '/icons/icon-192.png';
  const badge = options.badge || '/icons/badge.png';
  const tag = options.tag || '';
  const data = options.data || {};

  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      icon,
      badge,
      tag,
      data,
    })
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const urlToOpen = event.notification.data?.url || '/';

  event.waitUntil(
    self.clients.matchAll({
      type: 'window',
      includeUncontrolled: true,
    }).then((windowClients) => {
      // Check if there is already a window open with the relevant URL
      for (let i = 0; i < windowClients.length; i++) {
        const client = windowClients[i];
        if (
          'focus' in client &&
          client.url === urlToOpen &&
          'clients' in client
        ) {
          return client.focus();
        }
      }
      // If no window is open, open a new one
      if (self.clients.openWindow) {
        return self.clients.openWindow(urlToOpen);
      }
    })
  );
});
