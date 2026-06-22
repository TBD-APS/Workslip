import React from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'sonner';
import { queryClient } from '../lib/react-query';
import { AuthProvider } from './AuthContext';

export const AppProvider = ({ children }: { children: React.ReactNode }) => {
  return (
    <React.Suspense fallback={<div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>Henter...</div>}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          {children}
          <Toaster
            theme="dark"
            position="top-center"
            toastOptions={{
              style: {
                background: 'var(--surface-color)',
                border: '1px solid var(--surface-border)',
                backdropFilter: 'blur(20px)',
                color: 'var(--text-primary)'
              }
            }}
          />
        </AuthProvider>
      </QueryClientProvider>
    </React.Suspense>
  );
};
