import { useEffect, useState, useRef, type FormEvent } from 'react';
import { useParams, Link } from 'react-router-dom';
import { CheckCircle2, AlertTriangle, Loader2, LogIn, ArrowRight, User, Phone } from 'lucide-react';
import { verifyInviteToken } from '../api/inviteAccept';
import { AUTH_TOKEN_KEY, USER_EMAIL_KEY } from '../../../providers/authContextValue';
import {
  clearInviteEnrollmentSession,
  completeEntraInviteEnrollment,
  hasEntraCallback,
  loadInviteEnrollmentDraft,
  startEntraInviteSignIn,
} from '../api/entraInviteEnrollment';

type InviteState =
  | { status: 'checking' }
  | { status: 'invalid'; message: string }
  | { status: 'ready' }
  | { status: 'accepted' }
  | { status: 'authenticating' }
  | { status: 'enrolling' }
  | { status: 'error'; message: string }
  | { status: 'success' };

const errorMessages: Record<string, string> = {
  invite_consumed: 'Denne invitation er allerede blevet brugt.',
  invite_expired: 'Denne invitation er udløbet. Kontakt administratoren for en ny.',
  user_already_exists: 'Der findes allerede en bruger med denne e-mail. Prøv at log ind i stedet.',
};

const resolveInviteError = (errorCode: string | undefined, fallback: string) =>
  errorCode ? errorMessages[errorCode] ?? fallback : fallback;

export const InviteAccept = () => {
  const { token } = useParams<{ token: string }>();
  const [state, setState] = useState<InviteState>({ status: 'checking' });
  const [displayName, setDisplayName] = useState('');
  const [phone, setPhone] = useState('');
  const calledRef = useRef(false);

  useEffect(() => {
    if (hasEntraCallback()) {
      const draft = loadInviteEnrollmentDraft();
      if (draft) {
        setDisplayName(draft.displayName);
        setPhone(draft.phone ?? '');
      }

      setState({ status: 'enrolling' });
      completeEntraInviteEnrollment()
        .then(response => {
          sessionStorage.setItem(AUTH_TOKEN_KEY, response.token);
          sessionStorage.setItem(USER_EMAIL_KEY, response.user.email);
          clearInviteEnrollmentSession();
          window.history.replaceState(null, '', window.location.pathname);
          setState({ status: 'success' });
          window.location.assign('/app/profil');
        })
        .catch((err: unknown) => {
          window.history.replaceState(null, '', window.location.pathname);
          const errorCode = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
          const message = errorCode
            ? resolveInviteError(errorCode, 'Kunne ikke færdiggøre Microsoft login. Prøv igen.')
            : (err as Error)?.message || 'Kunne ikke færdiggøre Microsoft login. Prøv igen.';
          setState({ status: 'error', message });
        });
      return;
    }

    if (!token) {
      setState({ status: 'invalid', message: 'Manglende invitationslink.' });
      return;
    }

    if (calledRef.current) return;
    calledRef.current = true;

    verifyInviteToken(token)
      .then(() => {
        setState({ status: 'ready' });
      })
      .catch(() => setState({ status: 'invalid', message: 'Invitationen blev ikke fundet. Kontrollér linket eller kontakt administratoren.' }));
  }, [token]);

  const handleAcceptInvite = () => {
    setState({ status: 'accepted' });
  };

  const handleContinueToMicrosoft = async (e: FormEvent) => {
    e.preventDefault();
    if (!token || !displayName.trim()) return;
    setState({ status: 'authenticating' });

    try {
      await startEntraInviteSignIn({ token, displayName: displayName.trim(), phone: phone.trim() || undefined });
    } catch (err: unknown) {
      const errorCode = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
      const message = errorCode
        ? resolveInviteError(errorCode, 'Kunne ikke acceptere invitationen. Prøv igen.')
        : (err as Error)?.message || 'Kunne ikke acceptere invitationen. Prøv igen.';
      setState({ status: 'error', message });
    }
  };

  const isWorking = state.status === 'authenticating' || state.status === 'enrolling';
  const showDetailsForm = state.status === 'accepted' || state.status === 'error' || isWorking;

  return (
    <div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1" />
        <div className="bg-glow bg-glow-2" />
      </div>

      <div style={{
        width: '100%',
        maxWidth: '420px',
        padding: '2.5rem',
        background: 'var(--surface-color)',
        border: '1px solid var(--surface-border)',
        borderRadius: '24px',
        backdropFilter: 'blur(20px)',
        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5)',
        textAlign: 'center',
      }}>
        <div className="logo" style={{ justifyContent: 'center', marginBottom: '1.5rem' }}>
          <svg className="logo-icon" width="40" height="40" viewBox="0 0 24 24" fill="none">
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
        </div>

        {state.status === 'checking' && (
          <>
            <Loader2 className="animate-spin" size={32} style={{ marginBottom: '1rem' }} />
            <p style={{ color: 'var(--text-secondary)' }}>Kontrollerer invitation...</p>
          </>
        )}

        {state.status === 'invalid' && (
          <>
            <div style={{ marginBottom: '1.5rem' }}>
              <AlertTriangle size={48} style={{ color: '#ef4444' }} />
            </div>
            <h2 style={{ marginBottom: '0.5rem' }}>Ugyldig invitation</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '1.5rem' }}>
              {state.message}
            </p>
            <Link to="/login" className="btn btn-primary" style={{ textDecoration: 'none', display: 'inline-block' }}>
              Gå til login
            </Link>
          </>
        )}

        {state.status === 'ready' && (
          <>
            <div style={{ marginBottom: '1.5rem' }}>
              <CheckCircle2 size={48} style={{ color: '#22c55e' }} />
            </div>
            <h2 style={{ marginBottom: '0.5rem' }}>Du er inviteret til Workslip</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '2rem' }}>
              Acceptér invitationen for at oprette din konto. Derefter beder vi om dit navn og telefonnummer, før du fortsætter med Microsoft.
            </p>
            <button
              type="button"
              className="btn btn-primary"
              onClick={handleAcceptInvite}
              style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem' }}
            >
              Acceptér invitation
              <ArrowRight size={18} />
            </button>
          </>
        )}

        {state.status === 'success' ? (
          <>
            <div style={{ marginBottom: '1.5rem' }}>
              <CheckCircle2 size={48} style={{ color: '#22c55e' }} />
            </div>
            <h2 style={{ marginBottom: '0.5rem' }}>Velkommen til Workslip</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '2rem' }}>
              Din konto er blevet oprettet.
            </p>
            <a
              href="/app/profil"
              className="btn btn-primary"
              style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}
            >
              Gå til profil
              <ArrowRight size={18} />
            </a>
          </>
        ) : null}

        {showDetailsForm && (
          <>
            <h2 style={{ marginBottom: '0.5rem' }}>Færdiggør din konto</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '2rem' }}>
              Indtast dit fulde navn og telefonnummer. Bagefter sender vi dig til Microsoft for login og passkey-oprettelse.
            </p>

            {state.status === 'error' && (
              <div style={{
                padding: '0.75rem 1rem',
                background: 'rgba(239, 68, 68, 0.1)',
                border: '1px solid rgba(239, 68, 68, 0.2)',
                borderRadius: '12px',
                marginBottom: '1.5rem',
                color: '#ef4444',
                fontSize: '0.9rem',
                display: 'flex',
                alignItems: 'center',
                gap: '0.5rem',
                textAlign: 'left'
              }}>
                <AlertTriangle size={16} style={{ flexShrink: 0 }} />
                <span>{state.message}</span>
              </div>
            )}

            <form onSubmit={handleContinueToMicrosoft} style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              <div style={{ textAlign: 'left' }}>
                <label htmlFor="displayName" style={{ display: 'block', fontSize: '0.85rem', color: 'var(--text-secondary)', marginBottom: '0.35rem' }}>
                  Fulde navn
                </label>
                <div style={{ position: 'relative' }}>
                  <User size={16} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-tertiary)' }} />
                  <input
                    id="displayName"
                    type="text"
                    value={displayName}
                    onChange={e => setDisplayName(e.target.value)}
                    placeholder="Dit fulde navn"
                    required
                    disabled={isWorking}
                    style={{
                      width: '100%',
                      padding: '0.7rem 0.75rem 0.7rem 2.5rem',
                      background: 'var(--input-bg)',
                      border: '1px solid var(--input-border)',
                      borderRadius: '12px',
                      color: 'var(--text-primary)',
                      fontSize: '0.95rem',
                      outline: 'none',
                      boxSizing: 'border-box'
                    }}
                  />
                </div>
              </div>

              <div style={{ textAlign: 'left' }}>
                <label htmlFor="phone" style={{ display: 'block', fontSize: '0.85rem', color: 'var(--text-secondary)', marginBottom: '0.35rem' }}>
                  Telefonnummer <span style={{ color: 'var(--text-tertiary)' }}>(valgfrit)</span>
                </label>
                <div style={{ position: 'relative' }}>
                  <Phone size={16} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-tertiary)' }} />
                  <input
                    id="phone"
                    type="tel"
                    value={phone}
                    onChange={e => setPhone(e.target.value)}
                    placeholder="+45 12 34 56 78"
                    disabled={isWorking}
                    style={{
                      width: '100%',
                      padding: '0.7rem 0.75rem 0.7rem 2.5rem',
                      background: 'var(--input-bg)',
                      border: '1px solid var(--input-border)',
                      borderRadius: '12px',
                      color: 'var(--text-primary)',
                      fontSize: '0.95rem',
                      outline: 'none',
                      boxSizing: 'border-box'
                    }}
                  />
                </div>
              </div>

              <button
                type="submit"
                className="btn btn-primary"
                disabled={isWorking || !displayName.trim()}
                style={{
                  width: '100%',
                  marginTop: '0.5rem',
                  opacity: isWorking || !displayName.trim() ? 0.7 : 1,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: '0.5rem',
                  cursor: isWorking ? 'wait' : 'pointer'
                }}
              >
                {isWorking ? (
                  <Loader2 className="animate-spin" size={18} />
                ) : (
                  <LogIn size={18} />
                )}
                <span>{state.status === 'enrolling' ? 'Opretter konto...' : state.status === 'authenticating' ? 'Sender til Microsoft...' : 'Fortsæt med Microsoft'}</span>
              </button>
            </form>

            <div style={{ marginTop: '1.5rem', fontSize: '0.9rem' }}>
              <Link to="/login" style={{ color: 'var(--text-secondary)' }}>
                Har du allerede en konto? Log ind her
              </Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
};
