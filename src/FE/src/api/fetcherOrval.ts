import axios from "axios";
import type { AxiosRequestConfig } from "axios";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

export const AXIOS_INSTANCE = axios.create({
  baseURL: normalizeApiBaseUrl(apiBaseUrl),
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
  url: string,
  options?: RequestInit,
): Promise<T> => {
  const response = await AXIOS_INSTANCE.request<T>({
    url,
    method: options?.method as AxiosRequestConfig["method"],
    headers: options?.headers as AxiosRequestConfig["headers"],
    data: parseRequestBody(options?.body),
    signal: options?.signal ?? undefined,
  });

  return {
    data: response.data,
    status: response.status,
    headers: response.headers,
  } as T;
};

function parseRequestBody(body: BodyInit | null | undefined) {
  if (typeof body !== "string") return body;

  try {
    return JSON.parse(body);
  } catch {
    return body;
  }
}

function normalizeApiBaseUrl(baseUrl: string) {
  return baseUrl.replace(/\/+$/, "").replace(/\/api$/i, "");
}
