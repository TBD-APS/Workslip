import axios from "axios";
import qs from "qs";
import type { AxiosRequestConfig } from "axios";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

export const AXIOS_INSTANCE = axios.create({
  baseURL: apiBaseUrl,
   paramsSerializer: {
    serialize: (params) =>
      qs.stringify(params, {
        arrayFormat: "repeat",
      }),
  },
});

AXIOS_INSTANCE.interceptors.request.use((config) => {
  const token = localStorage.getItem("authToken");

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  config.headers.Accept = "application/json";

  return config;
});

AXIOS_INSTANCE.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401) {
      localStorage.removeItem("authToken");
      window.location.href = "/login";
    }
    return Promise.reject(error);
  },
);

export const customAxiosInstance = async <T>(
  config: AxiosRequestConfig,
  options?: AxiosRequestConfig,
): Promise<T> => {
  const response = await AXIOS_INSTANCE.request<T>({
    ...config,
    ...options,
  });

  return response.data;
};
