import { useState } from 'react';
import { User, Mail, Shield, PartyPopper, Pencil, Save, X, Loader2 } from 'lucide-react';
import { useLocation } from 'react-router-dom';
import { toast } from 'sonner';
import { useAuth } from '../../../providers/useAuth';
import { usePatchApiAuthMe } from '../../../api/generated/auth/auth';

const roleLabels: Record<string, string> = {
  Admin: 'Administrator',
  User: 'Bruger',
};

export const Profile = () => {
  const { user, updateUser } = useAuth();
  const location = useLocation();
  const fromInvite = (location.state as { fromInvite?: boolean })?.fromInvite;

  const [isEditing, setIsEditing] = useState(false);
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [phone, setPhone] = useState(user?.phone ?? '');
  const patchMutation = usePatchApiAuthMe();

  if (!user) return null;

  const handleStartEdit = () => {
    setDisplayName(user.displayName);
    setPhone(user.phone);
    setIsEditing(true);
  };

  const handleCancel = () => {
    setIsEditing(false);
  };

  const handleSave = async () => {
    if (!displayName.trim()) {
      toast.error('Navn skal udfyldes');
      return;
    }

    try {
      await patchMutation.mutateAsync({
        data: { displayName: displayName.trim(), phone: phone.trim() || null, role: null },
      });

      updateUser({ displayName: displayName.trim(), phone: phone.trim() || undefined });

      setIsEditing(false);
      toast.success('Profil opdateret');
    } catch {
      toast.error('Kunne ikke opdatere profilen. Prøv igen.');
    }
  };

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
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
              <div className="user-avatar user-avatar--large user-avatar--accent">
                {user.displayName.charAt(0).toUpperCase()}
              </div>
              <div>
                <div style={{ fontWeight: 600, fontSize: '1.1rem', color: 'var(--text-primary)' }}>{user.displayName}</div>
                <div style={{ color: 'var(--text-secondary)', fontSize: '0.88rem' }}>{user.email}</div>
              </div>
            </div>
            {!isEditing && (
              <button className="btn btn-secondary" type="button" onClick={handleStartEdit} aria-label="Rediger profil">
                <Pencil size={16} />
              </button>
            )}
          </div>

          <div style={{ height: '1px', background: 'var(--surface-border)' }} />

          {isEditing ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                <label htmlFor="profile-display-name" style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>Navn</label>
                <input
                  id="profile-display-name"
                  className="form-input"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  placeholder="Dit navn"
                />
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                <label htmlFor="profile-phone" style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>Telefon</label>
                <input
                  id="profile-phone"
                  className="form-input"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="Telefonnummer"
                />
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', color: 'var(--text-secondary)' }}>
                <Mail size={16} />
                <span style={{ color: 'var(--text-primary)' }}>{user.email}</span>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', color: 'var(--text-secondary)' }}>
                <Shield size={16} />
                <span style={{ color: 'var(--text-primary)' }}>{roleLabels[user.role] ?? user.role}</span>
              </div>
              <div style={{ display: 'flex', gap: '0.75rem', marginTop: '0.5rem' }}>
                <button className="btn btn-secondary" type="button" onClick={handleCancel} disabled={patchMutation.isPending}>
                  <X size={16} />
                  Annuller
                </button>
                <button className="btn btn-primary" type="button" onClick={handleSave} disabled={patchMutation.isPending}>
                  {patchMutation.isPending ? <Loader2 size={16} className="spin" /> : <Save size={16} />}
                  {patchMutation.isPending ? 'Gemmer...' : 'Gem'}
                </button>
              </div>
            </div>
          ) : (
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
          )}
        </div>
      </div>
    </div>
  );
};