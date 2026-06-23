import { BrowserRouter } from 'react-router-dom';
import { ErrorBoundary } from 'react-error-boundary';
import { SpeedInsights } from '@vercel/speed-insights/react';
import { Analytics } from '@vercel/analytics/react';
import { AppProvider } from './providers/AppProvider';
import { ErrorFallback } from './providers/ErrorFallback';
import { AppRoutes } from './routes';

import './index.css';
import './App.css';

function App() {
  return (
    <AppProvider>
      <BrowserRouter>
        {/* ErrorBoundary lives INSIDE the router so the fallback can
            use useNavigate. AppProvider still wraps everything so
            react-query / auth state survive a fallback render. */}
        <ErrorBoundary FallbackComponent={ErrorFallback}>
          <AppRoutes />
          <SpeedInsights />
          <Analytics />
        </ErrorBoundary>
      </BrowserRouter>
    </AppProvider>
  );
}

export default App;
