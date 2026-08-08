import { useEffect } from 'react';
import { LogOut } from 'lucide-react';
import { useAuth } from './useAuth';
import { reportFrontendError } from '../applicationInsights';

const APP_HOME_PATH = '/app';
const LOGIN_PATH = '/login';

export function ErrorFallback({ error, resetErrorBoundary }: { error: unknown; resetErrorBoundary: () => void }) {
  const { logout } = useAuth();
  const message = error instanceof Error ? error.message : String(error);

  useEffect(() => {
    reportFrontendError(error, 'react.error-boundary');
  }, [error]);

  const handleGoHome = () => {
    // A router navigation cannot recover while the surrounding ErrorBoundary is
    // still rendering this fallback. A hard navigation rebuilds the React tree
    // and preserves the current session.
    window.location.replace(APP_HOME_PATH);
  };

  const handleLogout = () => {
    logout();
    // Keep logout usable even if the router/error-boundary state itself is broken.
    window.location.replace(LOGIN_PATH);
  };

  return (
    <div className="app-container" style={{ justifyContent: 'center', alignItems: 'center', textAlign: 'center' }}>
      <div style={{ maxWidth: 400 }}>
        <h2>Noget gik galt</h2>
        <p style={{ color: 'var(--text-secondary)', marginBottom: '1rem' }}>
          Der opstod en uventet fejl
        </p>
        <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'center', flexWrap: 'wrap' }}>
          <button className="btn btn-primary" onClick={resetErrorBoundary}>Prøv igen</button>
          <button className="btn btn-secondary" onClick={handleGoHome}>Gå til forsiden</button>
          <button className="btn btn-secondary" onClick={handleLogout}>
            <LogOut size={16} /> Log ud
          </button>
        </div>
        {import.meta.env.DEV && (
          <details style={{ marginTop: '1rem', textAlign: 'left' }}>
            <summary>Detaljer</summary>
            <pre style={{ fontSize: '0.75rem', whiteSpace: 'pre-wrap' }}>{message}</pre>
          </details>
        )}
      </div>
    </div>
  );
}
