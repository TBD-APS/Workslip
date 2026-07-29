import { lazy, Suspense, useEffect, useState } from 'react';
import { RouterProvider } from 'react-router-dom';
import { AppProvider } from './providers/AppProvider';
import { router } from './routes';
import { scheduleAfterInitialLoad } from './lib/scheduleAfterInitialLoad';

import './public-fonts.css';
import './public-shell.css';

const VercelTelemetry = lazy(() =>
  import('./telemetry/VercelTelemetry').then((module) => ({ default: module.VercelTelemetry })),
);

function App() {
  const [telemetryEnabled, setTelemetryEnabled] = useState(false);

  useEffect(
    () => scheduleAfterInitialLoad(() => setTelemetryEnabled(true)),
    [],
  );

  return (
    <AppProvider>
      <RouterProvider router={router} />
      {telemetryEnabled && (
        <Suspense fallback={null}>
          <VercelTelemetry />
        </Suspense>
      )}
    </AppProvider>
  );
}

export default App;
