import { useRoutes, Navigate } from 'react-router-dom';
import { useAuth } from '../providers/AuthContext';
import { RoleGuard } from '../providers/permissions';
import { LandingPage } from '../features/landing/routes/LandingPage';
import { Login } from '../features/auth/routes/Login';
import { JobList } from '../features/jobs/routes/JobList';
import { JobDetail } from '../features/jobs/routes/JobDetail';
import { JobCreate } from '../features/jobs/routes/JobCreate';
import { AppLayout } from '../components/layouts/AppLayout';

const ProtectedRoute = ({ children }: { children: React.ReactNode }) => {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--text-secondary)' }}>Tjekker login status...</div>;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" />;
  }

  return <>{children}</>;
};

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
      element: <ProtectedRoute><AppLayout /></ProtectedRoute>,
      children: [
        { index: true, element: <JobList /> },
        { path: 'job/new', element: <RoleGuard permission="job:create"><JobCreate /></RoleGuard> },
        { path: 'job/:id', element: <JobDetail /> },
        // { path: 'settings', element: <Settings /> },
      ],
    },
  ]);

  return <>{routes}</>;
};
