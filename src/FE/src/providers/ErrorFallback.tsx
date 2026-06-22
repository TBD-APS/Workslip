import { useNavigate } from 'react-router-dom';

export function ErrorFallback({ error, resetErrorBoundary }: { error: unknown; resetErrorBoundary: () => void }) {
  const navigate = useNavigate();
  const message = error instanceof Error ? error.message : String(error);

  return (
    <div className="app-container" style={{ justifyContent: 'center', alignItems: 'center', textAlign: 'center' }}>
      <div style={{ maxWidth: 400 }}>
        <h2>Noget gik galt</h2>
        <p style={{ color: 'var(--text-secondary)', marginBottom: '1rem' }}>
          Der opstod en uventet fejl
        </p>
        <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'center' }}>
          <button className="btn btn-primary" onClick={resetErrorBoundary}>Prøv igen</button>
          <button className="btn btn-secondary" onClick={() => navigate('/')}>Gå til forsiden</button>
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
