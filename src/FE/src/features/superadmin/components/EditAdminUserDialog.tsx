import { useEffect, useState, type FormEvent } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';
import type { AdminUser, UpdateAdminUserInput } from '../types';
import { ROLE_OPTIONS } from './AdminUserCreateForm';

interface EditAdminUserDialogProps {
  user: AdminUser | null;
  isSubmitting: boolean;
  error: string | null;
  onSubmit: (input: UpdateAdminUserInput) => Promise<void>;
  onClose: () => void;
}

export function EditAdminUserDialog({ user, isSubmitting, error, onSubmit, onClose }: EditAdminUserDialogProps) {
  const [displayName, setDisplayName] = useState('');
  const [phone, setPhone] = useState('');
  const [role, setRole] = useState('');

  useEffect(() => {
    if (!user) return;
    setDisplayName(user.displayName);
    setPhone(user.phone);
    setRole(user.role);
  }, [user]);

  useEffect(() => {
    if (!user) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [user, onClose]);

  if (!user) return null;

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    await onSubmit({
      displayName: displayName.trim() || undefined,
      phone: phone.trim() || undefined,
      role: role || undefined,
    });
  };

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-label={`Rediger ${user.displayName}`}
      >
        <h3>Rediger bruger</h3>
        <p>{user.organizationName} · {user.email}</p>

        <form onSubmit={(event) => { void handleSubmit(event); }} noValidate>
          <div className="form-group">
            <label className="form-label" htmlFor="edit-admin-user-name">Navn</label>
            <input
              id="edit-admin-user-name"
              className="form-input"
              type="text"
              maxLength={256}
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="edit-admin-user-phone">Telefon</label>
            <input
              id="edit-admin-user-phone"
              className="form-input"
              type="tel"
              maxLength={20}
              value={phone}
              onChange={(event) => setPhone(event.target.value)}
            />
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="edit-admin-user-role">Rolle</label>
            <select
              id="edit-admin-user-role"
              className="form-input superadmin-select"
              value={role}
              onChange={(event) => setRole(event.target.value)}
            >
              {ROLE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>

          {error && (
            <div className="superadmin-alert superadmin-alert-error" role="alert">
              {error}
            </div>
          )}

          <div className="modal-actions">
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="animate-spin" size={16} aria-hidden="true" />}
              <span>{isSubmitting ? 'Gemmer...' : 'Gem ændringer'}</span>
            </button>
            <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSubmitting}>
              Annuller
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body,
  );
}
