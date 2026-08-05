import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { notify } from '../../../lib/toast';
import {
  Check,
  CheckCircle2,
  ChevronRight,
  Clock,
  FileText,
  Loader2,
  Mail,
  MailPlus,
  Plus,
  Send,
  Trash2,
  X,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { usePostApiAuthInvite } from '../../../api/generated/auth/auth';
import { useDeleteApiAuthInvite, useGetApiAuthInvites, type InviteTokenResponse } from '../api';

type InviteRole = 'User' | 'Auditor';

type InviteUserMutationResult = {
  email: string;
  success: boolean;
  error: string | null;
};

const INVALID_INVITE_RESPONSE_MESSAGE =
  'Invitationens resultat kunne ikke bekræftes. Genindlæs siden og prøv igen.';

const getInviteRoleLabel = (role: string | null) => role === 'Auditor' ? 'Auditør' : 'Medarbejder';

const getCompactInviteRoleLabel = (role: string | null) => role === 'Auditor' ? 'Auditør' : 'Medarb.';

const getInviteResults = (response: unknown): InviteUserMutationResult[] | null => {
  if (!response || typeof response !== 'object') return null;

  const results = (response as { results?: unknown }).results;
  return Array.isArray(results) ? results as InviteUserMutationResult[] : null;
};

export const Settings = () => {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [emails, setEmails] = useState<string[]>([]);
  const [inviteRole, setInviteRole] = useState<InviteRole>('User');
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [clearingInviteId, setClearingInviteId] = useState<string | null>(null);

  const invitesQuery = useGetApiAuthInvites();
  const inviteMutation = usePostApiAuthInvite();
  const clearInviteMutation = useDeleteApiAuthInvite();

  const isValidEmail = (e: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e);

  const handleAddEmail = () => {
    const trimmed = email.trim().toLowerCase();
    if (!trimmed) return;
    if (!isValidEmail(trimmed)) {
      notify.error('Ugyldig e-mail-adresse');
      return;
    }
    if (emails.includes(trimmed)) {
      notify.error('E-mail er allerede tilføjet');
      return;
    }
    setInviteError(null);
    setEmails((prev) => [...prev, trimmed]);
    setEmail('');
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      handleAddEmail();
    }
  };

  const handleRemoveEmail = (idx: number) => {
    setInviteError(null);
    setEmails((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleSendInvites = async () => {
    if (emails.length === 0) {
      notify.error('Tilføj mindst én e-mail');
      return;
    }

    setInviteError(null);

    try {
      const response: unknown = await inviteMutation.mutateAsync({
        data: {
          emails,
          role: inviteRole,
          inviteBaseUrl: window.location.origin,
        },
      });

      const results = getInviteResults(response);
      if (!results || results.length !== emails.length) {
        setInviteError(INVALID_INVITE_RESPONSE_MESSAGE);
        notify.error(INVALID_INVITE_RESPONSE_MESSAGE);
        return;
      }

      const failedResults = results.filter((result) => !result.success);
      const failedEmails = new Set(
        failedResults.map((result) => result.email.trim().toLowerCase()),
      );
      const successfulCount = results.length - failedResults.length;

      if (successfulCount > 0) {
        notify.success(
          successfulCount === 1
            ? '1 invitation sendt'
            : `${successfulCount} invitationer sendt`,
        );
      }

      if (failedResults.length > 0) {
        const firstError = failedResults.find((result) => result.error)?.error;
        const errorMessage = firstError
          ?? (failedResults.length === 1
            ? 'Invitationen kunne ikke sendes'
            : `${failedResults.length} invitationer kunne ikke sendes`);

        setInviteError(errorMessage);
        notify.error(errorMessage);
        setEmails((current) => current.filter((address) =>
          failedEmails.has(address.trim().toLowerCase())));
      } else {
        setEmails([]);
      }

      await queryClient.invalidateQueries({ queryKey: ['/api/auth/invites'] });
    } catch {
      const errorMessage = 'Kunne ikke sende invitationer';
      setInviteError(errorMessage);
      notify.error(errorMessage);
    }
  };

  const handleClearInvite = async (invite: InviteTokenResponse) => {
    const confirmed = window.confirm(
      `Ryd invitationsstatus for ${invite.email}? En invitation, der ikke er accepteret, bliver samtidig ugyldig.`,
    );
    if (!confirmed) return;

    setClearingInviteId(invite.id);
    try {
      await clearInviteMutation.mutateAsync(invite.id);
      await queryClient.invalidateQueries({ queryKey: ['/api/auth/invites'] });
      notify.success('Invitationsstatus er ryddet');
    } catch {
      notify.error('Kunne ikke rydde invitationsstatus');
    } finally {
      setClearingInviteId(null);
    }
  };

  const statusLabel = (invite: { consumed: boolean; openedAt: string | null; acceptedAt: string | null; expiresAt: string }) => {
    if (invite.consumed) return { label: 'Accepteret', icon: CheckCircle2, cls: 'status-accepted' };
    if (invite.acceptedAt) return { label: 'Accepteret', icon: CheckCircle2, cls: 'status-accepted' };
    if (invite.openedAt) return { label: 'Åbnet', icon: Check, cls: 'status-opened' };
    if (new Date(invite.expiresAt) < new Date()) return { label: 'Udløbet', icon: Clock, cls: 'status-expired' };
    return { label: 'Sendt', icon: Mail, cls: 'status-sent' };
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Administrativt</h2>
        <p className="subtitle">Administrer invitationer</p>
      </div>

      <div className="section-card">
        <h3 className="section-card-title">
          <MailPlus size={18} />
          Inviter brugere
        </h3>

        <div className="form-group invite-role-field">
          <label className="form-label" htmlFor="invite-role">
            Rolle for invitationerne
          </label>
          <select
            id="invite-role"
            className="form-input"
            value={inviteRole}
            onChange={(event) => {
              setInviteError(null);
              setInviteRole(event.target.value as InviteRole);
            }}
            disabled={inviteMutation.isPending}
          >
            <option value="User">Medarbejder</option>
            <option value="Auditor">Auditør</option>
          </select>
          <p className="form-help-text">
            Alle e-mailadresser i denne invitation får den valgte rolle.
          </p>
        </div>

        <div className="invite-input-row">
          <input
            type="email"
            className="form-input"
            placeholder="Skriv e-mail..."
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onKeyDown={handleKeyDown}
          />
          <button
            type="button"
            className="btn btn-primary invite-add-btn"
            onClick={handleAddEmail}
            disabled={!email.trim()}
            aria-label="Tilføj e-mail"
          >
            <Plus size={18} />
          </button>
        </div>

        {emails.length > 0 && (
          <div className="invite-email-list">
            {emails.map((e, i) => (
              <div key={e} className="invite-email-chip">
                <span>{e}</span>
                <button
                  type="button"
                  className="btn-icon btn-icon-danger"
                  onClick={() => handleRemoveEmail(i)}
                  aria-label={`Fjern ${e}`}
                >
                  <X size={16} />
                </button>
              </div>
            ))}
          </div>
        )}

        {inviteError && (
          <p className="form-error-text" role="alert">
            {inviteError}
          </p>
        )}

        <button
          type="button"
          className="btn btn-primary invite-send-btn"
          onClick={handleSendInvites}
          disabled={emails.length === 0 || inviteMutation.isPending}
        >
          {inviteMutation.isPending ? (
            <><Loader2 size={16} className="spin" /> Sender...</>
          ) : (
            <>
              <Send size={16} />
              Send invitation{emails.length !== 1 ? 'er' : ''}
            </>
          )}
        </button>
      </div>

      <div className="section-card" style={{ marginTop: '1rem' }}>
        <h3 className="section-card-title">
          <Mail size={18} />
          Invitationsstatus
        </h3>

        {invitesQuery.isLoading && (
          <p className="subtitle" style={{ padding: '1rem 0' }}>Henter invitationer...</p>
        )}

        {invitesQuery.isError && (
          <ErrorState message="Kunne ikke hente invitationer" />
        )}

        {invitesQuery.data && invitesQuery.data.invites.length === 0 && (
          <div className="empty-state">
            <p>Ingen invitationer endnu.</p>
          </div>
        )}

        {invitesQuery.data && invitesQuery.data.invites.length > 0 && (
          <div className="invite-status-list">
            {invitesQuery.data.invites.map((invite) => {
              const st = statusLabel(invite);
              const Icon = st.icon;
              const isClearing = clearingInviteId === invite.id;
              const roleLabel = getInviteRoleLabel(invite.role);
              return (
                <div key={invite.id} className="invite-status-row">
                  <div className="invite-status-content">
                    <span
                      className="invite-status-email"
                      title={invite.email}
                    >
                      {invite.email}
                    </span>
                    <span className={`invite-status-badge ${st.cls}`}>
                      <Icon size={12} />
                      {st.label}
                    </span>
                    <span
                      className="invite-role-badge"
                      title={roleLabel}
                    >
                      <span aria-hidden="true">{getCompactInviteRoleLabel(invite.role)}</span>
                      <span className="invite-role-full-label">Rolle: {roleLabel}</span>
                    </span>
                    <span className="invite-status-date">
                      {new Date(invite.createdAt).toLocaleDateString('da-DK', {
                        day: 'numeric',
                        month: 'short',
                        year: 'numeric',
                      })}
                    </span>
                  </div>
                  <div className="invite-status-action">
                    <button
                      type="button"
                      className="btn-icon btn-icon-danger invite-clear-btn"
                      onClick={() => void handleClearInvite(invite)}
                      disabled={clearInviteMutation.isPending}
                      aria-label={`Ryd invitationsstatus for ${invite.email}`}
                      title="Ryd invitationsstatus"
                    >
                      {isClearing ? <Loader2 size={15} className="spin" /> : <Trash2 size={15} />}
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <div className="section-card" style={{ marginTop: '1rem' }}>
        <h3 className="section-card-title">
          <FileText size={18} />
          Juridisk
        </h3>

        <div className="section-card-link">
          <Link to="/app/legal/terms">
            <span>Vilkår og betingelser</span>
            <ChevronRight size={16} />
          </Link>
        </div>

        <div className="section-card-link">
          <Link to="/app/legal/privacy">
            <span>Privatlivspolitik</span>
            <ChevronRight size={16} />
          </Link>
        </div>

        <div className="section-card-link">
          <Link to="/app/legal/cookies">
            <span>Cookiepolitik</span>
            <ChevronRight size={16} />
          </Link>
        </div>
      </div>
    </div>
  );
};
