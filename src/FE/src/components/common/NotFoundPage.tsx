import { Home } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

type NotFoundPageProps = {
  title?: string;
  message?: string;
  destination?: string;
  actionLabel?: string;
};

export function NotFoundPage({
  title = 'Siden blev ikke fundet',
  message = 'Linket findes ikke længere, eller adressen er forkert.',
  destination = '/app',
  actionLabel = 'Gå til forsiden',
}: NotFoundPageProps) {
  const navigate = useNavigate();

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>{title}</h2>
        <p className="subtitle">{message}</p>
      </div>
      <button
        type="button"
        className="btn btn-primary"
        onClick={() => navigate(destination, { replace: true })}
      >
        <Home size={16} aria-hidden="true" />
        {actionLabel}
      </button>
    </div>
  );
}
