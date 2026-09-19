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

function App() {
  return (
    <AppProvider>
      <RouterProvider router={router} />
      <GamificationFeedback />
    </AppProvider>
  );
}

export default App;
