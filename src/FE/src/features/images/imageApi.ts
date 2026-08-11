import { AXIOS_INSTANCE, customAxiosInstance } from '../../api/fetcherOrval';

export type ImageInfo = {
  id: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
};

export function listJobImages(jobId: string) {
  return customAxiosInstance<ImageInfo[]>({
    url: `/api/jobs/${jobId}/images`,
    method: 'GET',
  });
}

export function uploadJobImage(jobId: string, file: File) {
  const data = new FormData();
  data.append('file', file);

  return customAxiosInstance<ImageInfo>({
    url: `/api/jobs/${jobId}/images`,
    method: 'POST',
    data,
  });
}

export function deleteJobImage(jobId: string, imageId: string) {
  return customAxiosInstance<void>({
    url: `/api/jobs/${jobId}/images/${imageId}`,
    method: 'DELETE',
  });
}

export async function fetchJobImageBlob(jobId: string, imageId: string) {
  const response = await AXIOS_INSTANCE.get<Blob>(`/api/jobs/${jobId}/images/${imageId}`, {
    responseType: 'blob',
    headers: { Accept: 'image/*' },
    skipGlobalErrorToast: true,
  });

  return response.data;
}

export async function fetchProfileImageBlob(userId: string) {
  const response = await AXIOS_INSTANCE.get<Blob>(`/api/users/${userId}/profile-image`, {
    responseType: 'blob',
    headers: { Accept: 'image/*' },
    skipGlobalErrorToast: true,
  });

  return response.data;
}

export function uploadProfileImage(file: File) {
  const data = new FormData();
  data.append('file', file);

  return customAxiosInstance<void>({
    url: '/api/auth/me/profile-image',
    method: 'PUT',
    data,
    skipGlobalErrorToast: true,
  });
}

export function deleteProfileImage() {
  return customAxiosInstance<void>({
    url: '/api/auth/me/profile-image',
    method: 'DELETE',
    skipGlobalErrorToast: true,
  });
}
