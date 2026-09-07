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

export interface EconomicConnectionStatusResponse {
  available: boolean;
  connected: boolean;
  providerId: string;
  providerDisplayName: string;
  agreementNumber: string | null;
  companyName: string | null;
  connectedAt: string | null;
}

export interface EconomicConnectStartResponse {
  installationUrl: string;
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

export const getEconomicConnection = (signal?: AbortSignal) => {
  return customAxiosInstance<EconomicConnectionStatusResponse>(
    { url: '/api/accounting/economic/connection', method: 'GET', signal },
  );
};

export const startEconomicConnection = () => {
  return customAxiosInstance<EconomicConnectStartResponse>(
    { url: '/api/accounting/economic/connect', method: 'POST' },
  );
};

export const disconnectEconomic = () => {
  return customAxiosInstance<void>(
    { url: '/api/accounting/economic/connection', method: 'DELETE' },
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

export const useEconomicConnection = () => {
  return useQuery({
    queryKey: ['/api/accounting/economic/connection'],
    queryFn: ({ signal }) => getEconomicConnection(signal),
    staleTime: 30_000,
  });
};

export const useStartEconomicConnection = () => {
  return useMutation({ mutationFn: startEconomicConnection });
};

export const useDisconnectEconomic = () => {
  return useMutation({ mutationFn: disconnectEconomic });
};
