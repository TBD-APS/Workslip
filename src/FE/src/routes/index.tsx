import { useEffect, useState } from 'react';
import { Navigate, Outlet, createBrowserRouter } from 'react-router-dom';
import { ErrorBoundary } from 'react-error-boundary';
import { ErrorFallback } from '../providers/ErrorFallback';
import { useAuth } from '../providers/useAuth';
import { RoleGuard } from '../providers/permissions';
import { Login } from '../features/auth/routes/Login';
import { InviteAccept } from '../features/auth/routes/InviteAccept';
import { JobList } from '../features/jobs/routes/JobList';
import { JobDetail } from '../features/jobs/routes/JobDetail';
import { JobCreate } from '../features/jobs/routes/JobCreate';
import { CompletedJobReport } from '../features/jobs/routes/CompletedJobReport';
import { Create } from '../features/create/routes/Create';
import { UserList } from '../features/users/routes/UserList';
import { UserDetail } from '../features/users/routes/UserDetail';
import { CustomerList } from '../features/customers/routes/CustomerList';
import { CreateCustomerPage } from '../features/customers/routes/CreateCustomerPage';
import { EditCustomerPage } from '../features/customers/routes/EditCustomerPage';
import { AppLayout } from '../components/layouts/AppLayout';
import { MyWorksheets } from '../features/worksheets/routes/MyWorksheets';
import { CustomerDetail } from '../features/customers/routes/CustomerDetail';
import { Settings } from '../features/settings/routes/Settings';
import { AuditorReportList } from '../features/auditor/routes/AuditorReportList';
import { Profile } from '../features/settings/routes/Profile';

/**
 * Wraps every authenticated route. Waits through one short retry on a
 * transient `meQuery` failure (e.g. service-worker swap right after a deploy,
 * brief network blip) before declaring the user signed out. Without this, a
 * single failed `/api/auth/me` call logs the user out.
 */
const ProtectedRoute = ({ children }: { children: React.ReactNode }) => {
  const { isAuthenticated, isLoading, meQuery } = useAuth();
  const [retryUsed, setRetryUsed] = useState(false);

  useEffect(() => {
    if (retryUsed || !meQuery?.isError || meQuery.isPending) return undefined;
    const timer = setTimeout(() => {
      setRetryUsed(true);
      void meQuery.refetch();
    }, 500);
    return () => clearTimeout(timer);
  }, [meQuery?.isError, meQuery?.isPending, meQuery, retryUsed]);

  // Only show the "Tjekker login status..." spinner on the very first auth
  // check (no token yet, fetching /api/auth/me). If the user already has a
  // token and meQuery fails transiently, keep rendering the protected page
  // while the retry fires in the background — otherwise the user sees a
  // jarring flash of "logging in" text during a normal reauth flow.
  if (isLoading) {
    return <div className="protected-route-loading">Tjekker login status...</div>;
  }

  // Prevent redirecting while a retry is pending
  const isRetrying = meQuery?.isError && !retryUsed;
  if (isRetrying) {
    return <div className="protected-route-loading">Genforbinder til serveren...</div>;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" />;
  }

  return <>{children}</>;
};

function RootErrorBoundary() {
  return (
    <ErrorBoundary FallbackComponent={ErrorFallback}>
      <Outlet />
    </ErrorBoundary>
  );
}

export const router = createBrowserRouter([
  {
    element: <RootErrorBoundary />,
    children: [
      {
        path: '/',
        element: <Login />,
      },
      {
        path: '/login',
        element: <Login />,
      },
      {
        path: '/invite/callback',
        element: <InviteAccept />,
      },
      {
        path: '/invite/:token',
        element: <InviteAccept />,
      },
      {
        path: '/app',
        element: <ProtectedRoute><AppLayout /></ProtectedRoute>,
        children: [
          { index: true, element: <JobList /> },
          { path: 'timer', element: <MyWorksheets /> },
          { path: 'create', element: <RoleGuard permission="job:create"><Create /></RoleGuard> }, //"BIG BLUE BUTTON"
          { path: 'job/new', element: <RoleGuard permission="job-from-customer:create"><JobCreate /></RoleGuard> },
          { path: 'job/new', element: <RoleGuard permission="job:create"><JobCreate /></RoleGuard> },
          { path: 'job/:id', element: <JobDetail /> },
          { path: 'completed/:id', element: <CompletedJobReport /> },
          { path: 'users', element: <RoleGuard permission="user:manage"><UserList /></RoleGuard> },
          { path: 'users/:id', element: <RoleGuard permission="user:manage"><UserDetail /></RoleGuard> },
          { path: 'customers', element: <RoleGuard permission="customer:view"><CustomerList /></RoleGuard> },
          { path: 'customers/new', element: <CreateCustomerPage /> },
          { path: 'customers/:id', element: <CustomerDetail /> },
          { path: 'customers/:id/edit', element: <RoleGuard permission="user:manage"><EditCustomerPage /></RoleGuard> },
          { path: 'auditor', element: <RoleGuard permission="report:view"><AuditorReportList /></RoleGuard> },
          { path: 'profil', element: <Profile /> },
          { path: 'settings', element: <RoleGuard permission="user:manage"><Settings /></RoleGuard> },
        ],
      },
      {
        path: '*',
        element: <ErrorFallback error={new Error('Siden blev ikke fundet (404)')} resetErrorBoundary={() => {}} />,
      },
    ],
  },
]);
