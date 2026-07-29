import { useMutation, useQuery } from '@tanstack/react-query';
import { customAxiosInstance } from '../../api/fetcherOrval';

export interface InviteTokenResponse {
  id: string;
  email: string;
  role: string | null;
  createdAt: string;
  expiresAt: string;
  consumed: boolean;
  openedAt: string | null;
  acceptedAt: string | null;
}

export interface InviteListResponse {
  invites: InviteTokenResponse[];
}

export const getApiAuthInvites = (signal?: AbortSignal) => {
  return customAxiosInstance<InviteListResponse>(
    { url: `/api/auth/invites`, method: 'GET', signal },
  );
};

export const deleteApiAuthInvite = (inviteId: string) => {
  return customAxiosInstance<void>(
    { url: `/api/auth/invites/${inviteId}`, method: 'DELETE' },
  );
};

export const useGetApiAuthInvites = () => {
  return useQuery({
    queryKey: ['/api/auth/invites'],
    queryFn: ({ signal }) => getApiAuthInvites(signal),
  });
};

export const useDeleteApiAuthInvite = () => {
  return useMutation({
    mutationFn: deleteApiAuthInvite,
  });
};
