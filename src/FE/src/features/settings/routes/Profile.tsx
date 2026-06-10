import { User, Mail, Shield, PartyPopper } from 'lucide-react';
import { useLocation } from 'react-router-dom';
import { useAuth } from '../../../providers/useAuth';

const roleLabels: Record<string, string> = {
  Admin: 'Administrator',
  User: 'Bruger',
};

export const Profile = () => {
  const { user } = useAuth();
  const location = useLocation();
  const fromInvite = (location.state as { fromInvite?: boolean })?.fromInvite;

  if (!user) return null;

  return (
    <div className="page-container">
      {fromInvite && (
        <div style={{
          padding: '1rem 1.25rem',
          background: 'rgba(34, 197, 94, 0.1)',
          border: '1px solid rgba(34, 197, 94, 0.25)',
          borderRadius: '12px',
          marginBottom: '1rem',
          display: 'flex',
          alignItems: 'center',
          gap: '0.75rem',
          color: '#22c55e',
        }}>
          <PartyPopper size={20} />
          <div>
            <div style={{ fontWeight: 600 }}>Velkommen til Workslip!</div>
            <div style={{ fontSize: '0.85rem', opacity: 0.85 }}>Din konto er oprettet. Du er nu klar til at gå i gang.</div>
          </div>
        </div>
      )}

      <div className="page-header">
        <h2>Profil</h2>
        <p className="subtitle">Dine oplysninger</p>
      </div>

      <div className="section-card">
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
            <div className="user-avatar user-avatar--large user-avatar--accent">
              {user.displayName.charAt(0).toUpperCase()}
            </div>
            <div>
              <div style={{ fontWeight: 600, fontSize: '1.1rem', color: 'var(--text-primary)' }}>{user.displayName}</div>
              <div style={{ color: 'var(--text-secondary)', fontSize: '0.88rem' }}>{user.email}</div>
            </div>
          </div>

          <div style={{ height: '1px', background: 'var(--surface-border)' }} />

          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', color: 'var(--text-secondary)' }}>
              <User size={16} />
              <span style={{ color: 'var(--text-primary)' }}>{user.displayName}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', color: 'var(--text-secondary)' }}>
              <Mail size={16} />
              <span style={{ color: 'var(--text-primary)' }}>{user.email}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', color: 'var(--text-secondary)' }}>
              <Shield size={16} />
              <span style={{ color: 'var(--text-primary)' }}>{roleLabels[user.role] ?? user.role}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};