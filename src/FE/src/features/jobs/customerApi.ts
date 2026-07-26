import { customAxiosInstance } from '../../api/fetcherOrval';
import type { CustomerSearchViewModel } from '../../api/generated/models';

export function getApiCustomersFavorite(
  params?: { limit?: number | string },
) {
  return customAxiosInstance<CustomerSearchViewModel[]>(
    { url: `/api/customers/favorite`, method: 'GET', params },
  );
}

export function patchApiCustomersIdFavorite(
  id: string,
  data: { isFavorite: boolean },
) {
  return customAxiosInstance<void>(
    { url: `/api/customers/${id}/favorite`, method: 'PATCH', data },
  );
}
