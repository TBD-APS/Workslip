import { useRef, useState } from 'react';
import { User, Mail, Shield, PartyPopper, Pencil, Save, X, Loader2, Camera, Trash2 } from 'lucide-react';
import { useLocation } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { notify } from '../../../lib/toast';
import { useAuth } from '../../../providers/useAuth';
import { usePatchApiAuthMe, getGetApiAuthMeQueryKey } from '../../../api/generated/auth/auth';
import { deleteProfileImage, uploadProfileImage } from '../../images/imageApi';
import { ProfileAvatar, profileImageQueryKey, useProfileImage } from '../../images/ProfileAvatar';

const roleLabels: Record<string, string> = {
  Admin: 'Administrator',
  User: 'Bruger',
};

const MAX_PROFILE_IMAGE_SIZE = 10 * 1024 * 1024;
const PROFILE_IMAGE_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

export const Profile = () => {
  const { user, updateUser } = useAuth();
  const location = useLocation();
  const queryClient = useQueryClient();
  const fromInvite = (location.state as { fromInvite?: boolean })?.fromInvite;
  const profileInputRef = useRef<HTMLInputElement>(null);

  const [isEditing, setIsEditing] = useState(false);
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [phone, setPhone] = useState(user?.phone ?? '');
  const patchMutation = usePatchApiAuthMe();
  const profileImageQuery = useProfileImage(user?.id);

  const profileUploadMutation = useMutation({
    mutationFn: uploadProfileImage,
    onSuccess: async () => {
      if (user?.id) {
        await queryClient.invalidateQueries({ queryKey: profileImageQueryKey(user.id) });
      }
      notify.success('Profilbillede opdateret');
    },
    onError: () => notify.error('Kunne ikke uploade profilbilledet. Brug JPEG, PNG eller WebP på maks. 10 MB.'),
  });

  const profileDeleteMutation = useMutation({
    mutationFn: deleteProfileImage,
    onSuccess: async () => {
      if (user?.id) {
        queryClient.removeQueries({ queryKey: profileImageQueryKey(user.id) });
        await queryClient.invalidateQueries({ queryKey: profileImageQueryKey(user.id) });
      }
      notify.success('Profilbillede fjernet');
    },
    onError: () => notify.error('Kunne ikke fjerne profilbilledet. Prøv igen.'),
  });

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
      notify.error('Navn skal udfyldes');
      return;
    }

    try {
      await patchMutation.mutateAsync({
        data: { displayName: displayName.trim(), phone: phone.trim() || null, role: null },
      });

      updateUser({ displayName: displayName.trim(), phone: phone.trim() || undefined });
      queryClient.invalidateQueries({ queryKey: getGetApiAuthMeQueryKey() });

      setIsEditing(false);
      notify.success('Profil opdateret');
    } catch {
      notify.error('Kunne ikke opdatere profilen. Prøv igen.');
    }
  };

  const handleProfileImage = (fileList: FileList | null) => {
    const file = fileList?.[0];
    if (profileInputRef.current) profileInputRef.current.value = '';
    if (!file) return;

    if (!PROFILE_IMAGE_TYPES.has(file.type) || file.size <= 0 || file.size > MAX_PROFILE_IMAGE_SIZE) {
      notify.error('Brug et JPEG-, PNG- eller WebP-billede på maks. 10 MB.');
      return;
    }

    profileUploadMutation.mutate(file);
  };

  const profileImageBusy = profileUploadMutation.isPending || profileDeleteMutation.isPending;

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
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '0.75rem' }}>
            <div className="profile-avatar-editor">
              <div className="profile-avatar-preview">
                <ProfileAvatar
                  userId={user.id}
                  displayName={user.displayName}
                  blob={profileImageQuery.data}
                  alt={`Profilbillede for ${user.displayName}`}
                />
              </div>
              <div style={{ minWidth: 0 }}>
                <div style={{ fontWeight: 600, fontSize: '1.1rem', color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{user.displayName}</div>
                <div style={{ color: 'var(--text-secondary)', fontSize: '0.88rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{user.email}</div>
                <div className="profile-avatar-actions" style={{ marginTop: '0.6rem' }}>
                  <input
                    ref={profileInputRef}
                    className="sr-only"
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    onChange={(event) => handleProfileImage(event.target.files)}
                    disabled={profileImageBusy}
                  />
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => profileInputRef.current?.click()}
                    disabled={profileImageBusy}
                  >
                    {profileUploadMutation.isPending ? <Loader2 size={15} className="spin" /> : <Camera size={15} />}
                    {profileImageQuery.data ? 'Skift billede' : 'Tilføj billede'}
                  </button>
                  {profileImageQuery.data && (
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() => profileDeleteMutation.mutate()}
                      disabled={profileImageBusy}
                    >
                      {profileDeleteMutation.isPending ? <Loader2 size={15} className="spin" /> : <Trash2 size={15} />}
                      Fjern
                    </button>
                  )}
                </div>
              </div>
            </div>
            {!isEditing && (
              <button className="btn-icon" type="button" onClick={handleStartEdit} aria-label="Rediger profil" style={{ flexShrink: 0 }}>
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
              <div className="profile-edit-actions">
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
