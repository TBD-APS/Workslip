import { AlertCircle, LogOut } from 'lucide-react';
import { useAuth } from '../providers/useAuth';
import type { ReactNode } from 'react';

interface ErrorStateProps {
  message: string;
  onRetry?: () => void;
  children?: ReactNode;
}

export function ErrorState({ message, onRetry, children }: ErrorStateProps) {
  const { logout } = useAuth();

  const handleLogout = () => {
    logout();
  };

  return (
    <div className="error-state">
      <AlertCircle size={32} />
      <p>{message}</p>
      <div className="error-state-actions">
        {onRetry && (
          <button type="button" className="btn btn-primary" onClick={onRetry}>
            Prøv igen
          </button>
        )}
        {children}
        <button type="button" className="btn btn-secondary" onClick={handleLogout}>
          <LogOut size={16} /> Log ud
        </button>
      </div>
    </div>
  );
}
