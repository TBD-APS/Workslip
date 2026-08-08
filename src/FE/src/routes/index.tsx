import { WifiOff } from 'lucide-react';
import { lazy, Suspense, useEffect, useState } from 'react';
import { Navigate, Outlet, createBrowserRouter, useLocation, useNavigate } from 'react-router-dom';
import { ErrorBoundary } from 'react-error-boundary';
import { reportFrontendError } from '../applicationInsights';
import { FullscreenSystemState } from '../components/common/FullscreenSystemState';
import { NotFoundPage } from '../components/common/NotFoundPage';
import { Login } from '../features/auth/routes/Login';
import { ErrorFallback } from '../providers/ErrorFallback';
import { RoleGuard } from '../providers/permissions';
import { useAuth } from '../providers/useAuth';

const AUTH_STARTUP_GRACE_MS = 6_000;

const InviteAccept = lazy(() =>
  import('../features/auth/routes/InviteAccept').then((module) => ({ default: module.InviteAccept })),
);
const AppLayout = lazy(() =>
  import('../components/layouts/AppLayout').then((module) => ({ default: module.AppLayout })),
);
const JobList = lazy(() =>
  import('../features/jobs/routes/JobList').then((module) => ({ default: module.JobList })),
);
const JobCreate = lazy(() =>
  import('../features/jobs/routes/JobCreate').then((module) => ({ default: module.JobCreate })),
);
const SimpleJobCreate = lazy(() =>
  import('../features/jobs/routes/SimpleJobCreate').then((module) => ({ default: module.SimpleJobCreate })),
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
const SuperAdmin = lazy(() =>
  import('../features/superadmin/routes/SuperAdmin').then((module) => ({ default: module.SuperAdmin })),
);
const CacheDiagnostics = lazy(() =>
  import('../features/superadmin/routes/CacheDiagnostics').then((module) => ({ default: module.CacheDiagnostics })),
);

const loadJobEntryRoute = () =>
  import('../features/jobs/routes/JobEntryRoute').then((module) => ({ Component: module.JobEntryRoute }));

interface StartupRecoveryProps {
  isRetrying: boolean;
  onRetry: () => void;
  onReload: () => void;
  onLogin: () => void;
}

const StartupRecovery = ({ isRetrying, onRetry, onReload, onLogin }: StartupRecoveryProps) => (
  <FullscreenSystemState
    title="Forbindelsen tager længere tid end normalt"
    message="Serveren kan være ved at starte efter en deployment. Dit gemte login er ikke blevet slettet."
    isLoading={isRetrying}
    icon={<WifiOff size={28} />}
    role="alert"
    actions={(
      <>
        <button
          type="button"
          className="btn btn-primary"
          onClick={onRetry}
          disabled={isRetrying}
        >
          {isRetrying ? 'Prøver igen...' : 'Prøv igen'}
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={onReload}
          disabled={isRetrying}
        >
          Genindlæs appen
        </button>
        <button
          type="button"
          className="system-state-link"
          onClick={onLogin}
          disabled={isRetrying}
        >
          Log ind igen
        </button>
      </>
    )}
  />
);

/**
 * A stored token and a loaded user are separate states. Temporary API startup
 * failures must not clear a potentially valid session or leave the app spinning
 * indefinitely.
 */
const ProtectedRoute = ({ children }: { children: React.ReactNode }) => {
  const { hasAuthToken, isAuthenticated, isLoading, clearLocalSession, meQuery } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [startupTimedOut, setStartupTimedOut] = useState(false);
  const [retryAttempt, setRetryAttempt] = useState(0);
  const [isRetrying, setIsRetrying] = useState(false);

  const returnTo = `${location.pathname}${location.search}${location.hash}`;
  const loginUrl = `/login?returnTo=${encodeURIComponent(returnTo)}`;

  useEffect(() => {
    if (!hasAuthToken || isAuthenticated) return undefined;

    const timer = window.setTimeout(() => {
      setStartupTimedOut(true);
    }, AUTH_STARTUP_GRACE_MS);

    return () => window.clearTimeout(timer);
  }, [hasAuthToken, isAuthenticated, retryAttempt]);

  const handleRetry = async () => {
    setIsRetrying(true);
    setStartupTimedOut(false);
    setRetryAttempt((attempt) => attempt + 1);

    try {
      await meQuery.refetch();
    } catch {
      // Query state keeps the recovery screen visible.
    } finally {
      setIsRetrying(false);
    }
  };

  const handleLogin = () => {
    clearLocalSession();
    navigate(loginUrl, { replace: true });
  };

  if (isAuthenticated) {
    return <>{children}</>;
  }

  if (!hasAuthToken) {
    return <Navigate to={loginUrl} replace />;
  }

  if (startupTimedOut || meQuery.isError) {
    return (
      <StartupRecovery
        isRetrying={isRetrying}
        onRetry={() => { void handleRetry(); }}
        onReload={() => window.location.reload()}
        onLogin={handleLogin}
      />
    );
  }

  if (isLoading || meQuery.isPending) {
    return (
      <FullscreenSystemState
        title="Tjekker login"
        message="Vi kontrollerer din session og forbinder til Workslip."
      />
    );
  }

  return (
    <StartupRecovery
      isRetrying={isRetrying}
      onRetry={() => { void handleRetry(); }}
      onReload={() => window.location.reload()}
      onLogin={handleLogin}
    />
  );
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
      { path: '/', element: <Login /> },
      { path: '/login', element: <Login /> },
      { path: '/invite/callback', element: <InviteAccept /> },
      { path: '/invite/:token', element: <InviteAccept /> },
      {
        path: '/app',
        element: (
          <ProtectedRoute>
            <AppLayout />
          </ProtectedRoute>
        ),
        children: [
          { index: true, element: <JobList /> },
          { path: 'timer', element: <MyWorksheets /> },
          { path: 'create', element: <Create /> },
          { path: 'job/new', element: <RoleGuard permission="job:create"><JobCreate /></RoleGuard> },
          { path: 'job/simple/new', element: <RoleGuard permission="job:create"><SimpleJobCreate /></RoleGuard> },
          { path: 'job/:id', lazy: loadJobEntryRoute },
          { path: 'completed/:id', lazy: loadJobEntryRoute },
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
        path: '/superadmin',
        element: (
          <ProtectedRoute>
            <AppLayout />
          </ProtectedRoute>
        ),
        children: [
          {
            index: true,
            element: (
              <RoleGuard permission="organization:manage" redirectTo="/app">
                <SuperAdmin />
              </RoleGuard>
            ),
          },
          {
            path: 'cache',
            element: (
              <RoleGuard permission="organization:manage" redirectTo="/app">
                <CacheDiagnostics />
              </RoleGuard>
            ),
          },
        ],
      },
      {
        path: '*',
        element: <NotFoundPage />,
      },
    ],
  },
]);
