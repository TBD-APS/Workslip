import { BrowserRouter, Routes, Route } from 'react-router-dom';
import LandingPage from './pages/LandingPage';
import Login from './pages/Login';
import AppLayout from './layouts/AppLayout';
import JobList from './pages/app/JobList';

import './index.css';
import './app.css';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/login" element={<Login />} />
        <Route path="/app" element={<AppLayout />}>
          <Route index element={<JobList />} />
          {/* Future routes: /app/job/:id, /app/settings */}
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
