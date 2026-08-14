export const jobImagesQueryKey = (jobId: string) => ['job-images', jobId] as const;

export const jobImageBlobQueryKey = (jobId: string, imageId: string) =>
  ['job-image', jobId, imageId] as const;

export const profileImageQueryKey = (userId: string) => ['profile-image', userId] as const;
