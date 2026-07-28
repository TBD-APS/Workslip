import { useEffect, useState } from 'react';
import { Navigate, Outlet, createBrowserRouter, useLocation, useNavigate } from 'react-router-dom';
import { ErrorBoundary } from 'react-error-boundary';
import { ErrorFallback } from '../providers/ErrorFallback';
import { useAuth } from '../providers/useAuth';
import { RoleGuard } from '../providers/permissions';
import { Login } from '../features/auth/routes/Login';
import { InviteAccept } from '../features/auth/routes/InviteAccept';
import { JobList } from '../features/jobs/routes/JobList';
import { JobDetail } from '../features/jobs/routes/JobDetail';
import { JobCreate } from '../features/jobs/routes/JobCreate';
import { SimpleJobCreate } from '../features/jobs/routes/SimpleJobCreate';
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
import { LegalPage } from '../features/legal';
import { reportFrontendError } from '../applicationInsights';

const AUTH_STARTUP_GRACE_MS = 15_000;

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
        <p>
          Serveren kan være ved at starte efter en genstart eller deployment. Dit gemte login er ikke blevet slettet.
        </p>
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
 * Wraps every authenticated route. A stored token and a loaded user are
 * intentionally treated as separate states: temporary API unavailability must
 * not be interpreted as a logout. Startup is bounded and transitions to an
 * explicit recovery screen instead of an endless spinner.
 */
const ProtectedRoute = ({ children }: { children: React.ReactNode }) => {
  const { hasAuthToken, isAuthenticated, isLoading, logout, meQuery } = useAuth();
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
      // The query state keeps the recovery screen visible. Avoid an unhandled
      // rejection if a future query configuration enables throwOnError.
    } finally {
      setIsRetrying(false);
    }
  };

  const handleLogin = () => {
    logout();
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

  // Defensive fallback for an impossible token-without-user query state. Never
  // leave authenticated routes blank or redirect away from a potentially valid
  // session without an explicit user action.
  return (
    <StartupRecovery
      isRetrying={isRetrying}
      onRetry={() => { void handleRetry(); }}
      onReload={() => window.location.reload()}
      onLogin={handleLogin}
    />
  );
};

function RootErrorBoundary() {
  return (
    <ErrorBoundary
      FallbackComponent={ErrorFallback}
      onError={(error, info) => reportFrontendError(error, 'react.error-boundary', { componentStack: info.componentStack ?? '' })}
    >
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
