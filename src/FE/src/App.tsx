import { RouterProvider } from 'react-router-dom';
import { SpeedInsights } from '@vercel/speed-insights/react';
import { Analytics } from '@vercel/analytics/react';
import { AppProvider } from './providers/AppProvider';
import { router } from './routes';

import './index.css';
import './App.css';

function App() {
  return (
    <AppProvider>
      <RouterProvider router={router} />
      <SpeedInsights />
      <Analytics />
    </AppProvider>
  );
}

export default App;
