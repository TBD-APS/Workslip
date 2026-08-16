import axios from 'axios';

type UploadProblem = {
  detail?: string;
  message?: string;
  errors?: Record<string, string[]>;
};

type UploadErrorOptions = {
  fallback: string;
  tooLarge: string;
};

export function getUploadErrorMessage(error: unknown, options: UploadErrorOptions): string {
  if (!axios.isAxiosError(error)) return options.fallback;

  if (error.response?.status === 413) return options.tooLarge;

  const data = error.response?.data as UploadProblem | undefined;
  const validationMessage = data?.errors
    ? Object.values(data.errors).flat().find((message) => typeof message === 'string' && message.trim().length > 0)
    : undefined;

  return validationMessage
    ?? data?.message
    ?? data?.detail
    ?? options.fallback;
}
