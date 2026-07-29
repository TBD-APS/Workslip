import { lazy, Suspense, useEffect, useState } from 'react';
import { Navigate, Outlet, createBrowserRouter, useLocation, useNavigate } from 'react-router-dom';
import { ErrorBoundary } from 'react-error-boundary';
import { ErrorFallback } from '../providers/ErrorFallback';
import { useAuth } from '../providers/useAuth';
import { RoleGuard } from '../providers/permissions';
import { Login } from '../features/auth/routes/Login';
import { InviteAccept } from '../features/auth/routes/InviteAccept';
import { reportFrontendError } from '../applicationInsights';

const AUTH_STARTUP_GRACE_MS = 6_000;

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

interface StartupRecoveryProps {
  isRetrying: boolean;
  onRetry: () => void;
  onReload: () => void;
  onLogin: () => void;
}

const StartupRecovery = ({ isRetrying, onRetry, onReload, onLogin }: StartupRecoveryProps) => (
  <div className="app-container app-container-center" role="alert" aria-live="polite">
    <div className="login-card">
      <div className="login-card-header">
        <h2>Forbindelsen tager længere tid end normalt</h2>
        <p>Serveren kan være ved at starte efter en deployment. Dit gemte login er ikke blevet slettet.</p>
      </div>
      <div className="login-email-step">
        <button
          type="button"
          className="btn btn-primary login-submit-btn"
          onClick={onRetry}
          disabled={isRetrying}
        >
          {isRetrying ? 'Prøver igen...' : 'Prøv igen'}
        </button>
        <button
          type="button"
          className="btn btn-secondary login-submit-btn"
          onClick={onReload}
          disabled={isRetrying}
        >
          Genindlæs appen
        </button>
        <button
          type="button"
          className="login-back-btn"
          onClick={onLogin}
          disabled={isRetrying}
        >
          Log ind igen
        </button>
      </div>
    </div>
  </div>
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
      <div className="protected-route-loading" role="status" aria-live="polite">
        Tjekker login status...
      </div>
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
        element: <ProtectedRoute><AppLayout /></ProtectedRoute>,
        children: [
          { index: true, element: <JobList /> },
          { path: 'timer', element: <MyWorksheets /> },
          { path: 'create', element: <Create /> },
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