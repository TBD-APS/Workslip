import { lazy, Suspense, useEffect, useState, type ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../lib/react-query';
import { scheduleDeferredTelemetry } from '../lib/scheduleAfterInitialLoad';
import { AuthProvider } from './AuthContext';
import { ThemeProvider } from './ThemeProvider';

const ThemedToaster = lazy(() =>
  import('../components/common/ThemedToaster').then((module) => ({ default: module.ThemedToaster })),
);

export const AppProvider = ({ children }: { children: ReactNode }) => {
  const [toasterEnabled, setToasterEnabled] = useState(false);

  useEffect(
    () => scheduleDeferredTelemetry(() => setToasterEnabled(true)),
    [],
  );

  return (
    <Suspense fallback={<div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>Henter...</div>}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <AuthProvider>
            {children}
            {toasterEnabled && (
              <Suspense fallback={null}>
                <ThemedToaster />
              </Suspense>
            )}
          </AuthProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </Suspense>
  );
};
