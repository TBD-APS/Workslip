import Axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { toast } from 'sonner';

export const apiClient = Axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
});

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  // Add auth token here if available
  // const token = storage.getToken();
  // if (token) {
  //   config.headers.Authorization = `Bearer ${token}`;
  // }
  config.headers.Accept = 'application/json';
  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    return response.data;
  },
  (error) => {
    const message = error.response?.data?.message || error.message;
    
    // Handle specific backend error patterns (from AGENTS.md rules)
    if (error.response?.status === 401) {
      toast.error('Log ind for at fortsætte');
      // window.location.assign('/login');
    } else if (error.response?.status === 403) {
      toast.error('Du har ikke adgang til denne handling');
    } else if (error.response?.status === 400 && error.response?.data?.errors) {
      // ValidationProblem from backend
      toast.error('Ugyldig indtastning. Tjek venligst felterne.');
    } else if (error.response?.status === 409) {
      // Conflict
      toast.error(`Konflikt: ${error.response.data.error || message}`);
    } else {
      toast.error(message || 'Der opstod en uventet fejl');
    }
    
    return Promise.reject(error);
  }
);
