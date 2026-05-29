import React from 'react';
import { ErrorBoundary } from 'react-error-boundary';
import { QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'sonner';
import { queryClient } from '../lib/react-query';

const ErrorFallback = () => {
  return (
    <div className="app-container" style={{ justifyContent: 'center', alignItems: 'center', padding: '2rem', textAlign: 'center' }}>
      <h2 style={{ marginBottom: '1rem' }}>Hov, der skete en fejl!</h2>
      <p style={{ color: 'var(--text-secondary)', marginBottom: '2rem' }}>
        Noget gik galt. Prøv at genindlæse siden.
      </p>
      <button 
        className="btn btn-primary" 
        onClick={() => window.location.assign(window.location.origin)}
      >
        Genindlæs
      </button>
    </div>
  );
};

export const AppProvider = ({ children }: { children: React.ReactNode }) => {
  return (
    <React.Suspense fallback={<div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>Henter...</div>}>
      <ErrorBoundary FallbackComponent={ErrorFallback}>
        <QueryClientProvider client={queryClient}>
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
        </QueryClientProvider>
      </ErrorBoundary>
    </React.Suspense>
  );
};
