import { useQuery } from '@tanstack/react-query';
import { fetchProfileImageBlob } from './imageApi';
import { profileImageQueryKey } from './imageQueryKeys';

export function useProfileImage(userId: string | undefined) {
  return useQuery({
    queryKey: profileImageQueryKey(userId ?? ''),
    queryFn: () => fetchProfileImageBlob(userId!),
    enabled: Boolean(userId),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}
