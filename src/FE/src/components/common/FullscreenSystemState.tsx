import { Loader2 } from 'lucide-react';
import type { ReactNode } from 'react';
import './FullscreenSystemState.css';

interface FullscreenSystemStateProps {
  title: string;
  message: string;
  isLoading?: boolean;
  icon?: ReactNode;
  actions?: ReactNode;
  role?: 'status' | 'alert';
}

export function FullscreenSystemState({
  title,
  message,
  isLoading = true,
  icon,
  actions,
  role = 'status',
}: FullscreenSystemStateProps) {
  const indicator = isLoading
    ? <Loader2 className="animate-spin" size={30} aria-hidden="true" />
    : icon;

  return (
    <div
      className="app-container app-container-center system-state"
      role={role}
      aria-live="polite"
      aria-busy={isLoading}
    >
      <div className="bg-glow-wrapper" aria-hidden="true">
        <div className="bg-glow bg-glow-1" />
        <div className="bg-glow bg-glow-2" />
      </div>

      <section className="system-state-card">
        <div className="system-state-brand">
          <svg
            className="logo-icon"
            width="30"
            height="30"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <span>Workslip</span>
        </div>

        {indicator && (
          <div className="system-state-indicator" aria-hidden="true">
            {indicator}
          </div>
        )}

        <div className="system-state-copy">
          <h1>{title}</h1>
          <p>{message}</p>
        </div>

        {actions && <div className="system-state-actions">{actions}</div>}
      </section>
    </div>
  );
}
