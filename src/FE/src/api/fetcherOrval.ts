import type { AxiosRequestConfig } from "axios";
import { apiClient } from "../lib/axios";

export const AXIOS_INSTANCE = apiClient;

export const customAxiosInstance = async <T>(
  config: AxiosRequestConfig,
  options?: AxiosRequestConfig,
): Promise<T> => {
  return AXIOS_INSTANCE.request<T>({
    ...config,
    ...options,
  }) as Promise<T>;
};
