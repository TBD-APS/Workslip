import { lazy, Suspense, useEffect, useState } from 'react';
import { Navigate, Outlet, createBrowserRouter } from 'react-router-dom';
import { ErrorBoundary } from 'react-error-boundary';
import { ErrorFallback } from '../providers/ErrorFallback';
import { useAuth } from '../providers/useAuth';
import { RoleGuard } from '../providers/permissions';
import { Login } from '../features/auth/routes/Login';
import { InviteAccept } from '../features/auth/routes/InviteAccept';
import { reportFrontendError } from '../applicationInsights';

const AppLayout = lazy(() =>
  import('../components/layouts/AppLayout').then((module) => ({ default: module.AppLayout })),
);
const JobList = lazy(() =>
  import('../features/jobs/routes/JobList').then((module) => ({ default: module.JobList })),
);
const JobDetail = lazy(() =>
  import('../features/jobs/routes/JobDetail').then((module) => ({ default: module.JobDetail })),
);
const JobCreate = lazy(() =>
  import('../features/jobs/routes/JobCreate').then((module) => ({ default: module.JobCreate })),
);
const SimpleJobCreate = lazy(() =>
  import('../features/jobs/routes/SimpleJobCreate').then((module) => ({ default: module.SimpleJobCreate })),
);
const CompletedJobReport = lazy(() =>
  import('../features/jobs/routes/CompletedJobReport').then((module) => ({ default: module.CompletedJobReport })),
);
const Create = lazy(() =>
  import('../features/create/routes/Create').then((module) => ({ default: module.Create })),
);
const UserList = lazy(() =>
  import('../features/users/routes/UserList').then((module) => ({ default: module.UserList })),
);
const UserDetail = lazy(() =>
  import('../features/users/routes/UserDetail').then((module) => ({ default: module.UserDetail })),
);
const CustomerList = lazy(() =>
  import('../features/customers/routes/CustomerList').then((module) => ({ default: module.CustomerList })),
);
const CreateCustomerPage = lazy(() =>
  import('../features/customers/routes/CreateCustomerPage').then((module) => ({ default: module.CreateCustomerPage })),
);
const EditCustomerPage = lazy(() =>
  import('../features/customers/routes/EditCustomerPage').then((module) => ({ default: module.EditCustomerPage })),
);
const MyWorksheets = lazy(() =>
  import('../features/worksheets/routes/MyWorksheets').then((module) => ({ default: module.MyWorksheets })),
);
const CustomerDetail = lazy(() =>
  import('../features/customers/routes/CustomerDetail').then((module) => ({ default: module.CustomerDetail })),
);
const Settings = lazy(() =>
  import('../features/settings/routes/Settings').then((module) => ({ default: module.Settings })),
);
const AuditorReportList = lazy(() =>
  import('../features/auditor/routes/AuditorReportList').then((module) => ({ default: module.AuditorReportList })),
);
const Profile = lazy(() =>
  import('../features/settings/routes/Profile').then((module) => ({ default: module.Profile })),
);
const LegalPage = lazy(() =>
  import('../features/legal').then((module) => ({ default: module.LegalPage })),
);

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

function RouteLoadingFallback() {
  return <div className="protected-route-loading">Indlæser siden...</div>;
}

function RootErrorBoundary() {
  return (
    <ErrorBoundary
      FallbackComponent={ErrorFallback}
      onError={(error, info) => reportFrontendError(error, 'react.error-boundary', { componentStack: info.componentStack ?? '' })}
      onReset={() => window.location.reload()}
    >
      <Suspense fallback={<RouteLoadingFallback />}>
        <Outlet />
      </Suspense>
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
          { path: 'create', element: <Create /> }, // "BIG BLUE BUTTON"
          { path: 'job/new', element: <RoleGuard permission="job:create"><JobCreate /></RoleGuard> },
          { path: 'job/simple/new', element: <RoleGuard permission="job:create"><SimpleJobCreate /></RoleGuard> },
          { path: 'job/:id', element: <JobDetail /> },
          { path: 'completed/:id', element: <CompletedJobReport /> },
          { path: 'users', element: <RoleGuard permission="user:manage"><UserList /></RoleGuard> },
          { path: 'users/:id', element: <RoleGuard permission="user:manage"><UserDetail /></RoleGuard> },
          { path: 'customers', element: <RoleGuard permission="customer:view"><CustomerList /></RoleGuard> },
          { path: 'customers/new', element: <RoleGuard permission="customer:edit"><CreateCustomerPage /></RoleGuard> },
          { path: 'customers/:id', element: <RoleGuard permission="customer:view"><CustomerDetail /></RoleGuard> },
          { path: 'customers/:id/edit', element: <RoleGuard permission="user:manage"><EditCustomerPage /></RoleGuard> },
          { path: 'auditor', element: <RoleGuard permission="report:view"><AuditorReportList /></RoleGuard> },
          { path: 'profil', element: <Profile /> },
          { path: 'settings', element: <RoleGuard permission="user:manage"><Settings /></RoleGuard> },
          { path: 'legal/:type', element: <LegalPage /> },
        ],
      },
      {
        path: '*',
        element: <ErrorFallback error={new Error('Siden blev ikke fundet (404)')} resetErrorBoundary={() => {}} />,
      },
    ],
  },
]);