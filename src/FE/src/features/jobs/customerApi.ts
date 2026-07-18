import { customAxiosInstance } from '../../api/fetcherOrval';
import type { CustomerSearchViewModel } from '../../api/generated/models';

export function getApiCustomersTop(
  params?: { limit?: number | string },
) {
  return customAxiosInstance<CustomerSearchViewModel[]>(
    { url: `/api/customers/top`, method: 'GET', params },
  );
}

export function patchApiCustomersIdTop(
  id: string,
  data: { isTop: boolean },
) {
  return customAxiosInstance<void>(
    { url: `/api/customers/${id}/top`, method: 'PATCH', data },
  );
}
