import React from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'sonner';
import { queryClient } from '../lib/react-query';
import { AuthProvider } from './AuthContext';
import { ThemeProvider, useTheme } from './ThemeProvider';

const ToasterWithTheme = () => {
  const { theme } = useTheme();
  return (
    <Toaster
      theme={theme === 'night' ? 'dark' : 'light'}
      position="top-center"
      offset="calc(env(safe-area-inset-top, 0px) + 1rem)"
      toastOptions={{
        style: {
          background: 'var(--surface-color)',
          border: '1px solid var(--surface-border)',
          backdropFilter: 'blur(20px)',
          color: 'var(--text-primary)'
        }
      }}
    />
  );
};

export const AppProvider = ({ children }: { children: React.ReactNode }) => {
  return (
    <React.Suspense fallback={<div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>Henter...</div>}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <AuthProvider>
            {children}
            <ToasterWithTheme />
          </AuthProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </React.Suspense>
  );
};
