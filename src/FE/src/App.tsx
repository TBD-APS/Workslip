import { lazy, Suspense } from 'react';
import { RouterProvider } from 'react-router-dom';
import { GamificationFeedback } from './components/common/GamificationFeedback';
import { AppProvider } from './providers/AppProvider';
import { router } from './routes';

import './public-fonts.css';
import './public-shell.css';
import './public-error.css';
import './public-performance.css';
import './workslip-brand.css';
import './gamification-feedback.css';

const HelpWizard = lazy(() =>
  import('./features/platformFlags/HelpWizard').then((module) => ({ default: module.HelpWizard })),
);

function App() {
  return (
    <AppProvider>
      <RouterProvider router={router} />
      <GamificationFeedback />
      <Suspense fallback={null}>
        <HelpWizard />
      </Suspense>
    </AppProvider>
  );
}

export default App;
