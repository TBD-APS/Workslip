import { useRoutes } from 'react-router-dom';
import { LandingPage } from '../features/landing/routes/LandingPage';
import { Login } from '../features/auth/routes/Login';
import { JobList } from '../features/jobs/routes/JobList';
import { AppLayout } from '../components/layouts/AppLayout';

export const AppRoutes = () => {
  const routes = useRoutes([
    {
      path: '/',
      element: <LandingPage />,
    },
    {
      path: '/login',
      element: <Login />,
    },
    {
      path: '/app',
      element: <AppLayout />,
      children: [
        { index: true, element: <JobList /> },
        // { path: 'job/:id', element: <JobDetails /> },
        // { path: 'settings', element: <Settings /> },
      ],
    },
  ]);

  return <>{routes}</>;
};
