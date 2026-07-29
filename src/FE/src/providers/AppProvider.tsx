import { lazy, Suspense, useEffect, useState, type ReactNode } from 'react';
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
  );
};
