import { lazy, Suspense, useEffect, useState } from 'react';
import { RouterProvider } from 'react-router-dom';
import { GamificationFeedback } from './components/common/GamificationFeedback';
import { AppProvider } from './providers/AppProvider';
import { router } from './routes';
import { scheduleDeferredTelemetry } from './lib/scheduleAfterInitialLoad';

import './public-fonts.css';
import './public-shell.css';
import './public-error.css';
import './public-performance.css';
import './workslip-brand.css';
import './gamification-feedback.css';

const VercelTelemetry = lazy(() =>
  import('./telemetry/VercelTelemetry').then((module) => ({ default: module.VercelTelemetry })),
);

const HelpWizard = lazy(() =>
  import('./features/platformFlags/HelpWizard').then((module) => ({ default: module.HelpWizard })),
);

function App() {
  const [telemetryEnabled, setTelemetryEnabled] = useState(false);

  useEffect(
    () => scheduleDeferredTelemetry(() => setTelemetryEnabled(true)),
    [],
  );

  return (
    <AppProvider>
      <RouterProvider router={router} />
      <GamificationFeedback />
      <Suspense fallback={null}>
        <HelpWizard />
      </Suspense>
      {telemetryEnabled && (
        <Suspense fallback={null}>
          <VercelTelemetry />
        </Suspense>
      )}
    </AppProvider>
  );
}

export default App;
