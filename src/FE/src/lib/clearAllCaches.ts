import { apiClient } from './axios';
import { queryClient } from './react-query';

export async function clearAllCaches(): Promise<void> {
  await apiClient.post('/api/admin/cache/clear');

  queryClient.clear();

  if ('serviceWorker' in navigator) {
    const registrations = await navigator.serviceWorker.getRegistrations();
    await Promise.all(registrations.map((r) => r.unregister()));
  }

  if ('caches' in window) {
    const cacheNames = await caches.keys();
    await Promise.all(cacheNames.map((name) => caches.delete(name)));
  }

  window.location.reload();
}
