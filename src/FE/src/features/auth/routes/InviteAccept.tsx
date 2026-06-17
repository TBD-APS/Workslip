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
      .then((res) => {
        if (res.userExists) {
          // If user already exists, skip the enrollment form and go straight to login
          window.location.assign(`/login?email=${encodeURIComponent(res.email)}`);
          return;
        }
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
    <div className="app-container invite-container">
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1" />
        <div className="bg-glow bg-glow-2" />
      </div>

      <div className="invite-card">
        <div className="logo invite-logo-wrapper">
          <svg className="logo-icon" width="40" height="40" viewBox="0 0 24 24" fill="none">
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
        </div>

        {state.status === 'checking' && (
          <>
            <Loader2 className="animate-spin invite-status-icon" size={32} />
            <p className="invite-text">Kontrollerer invitation...</p>
          </>
        )}

        {state.status === 'invalid' && (
          <>
            <div className="invite-status-icon">
              <AlertTriangle size={48} className="invite-status-icon--danger" />
            </div>
            <h2 className="invite-title">Ugyldig invitation</h2>
            <p className="invite-text">
              {state.message}
            </p>
            <Link to="/login" className="btn btn-primary">
              Gå til login
            </Link>
          </>
        )}

        {state.status === 'ready' && (
          <>
            <div className="invite-status-icon">
              <CheckCircle2 size={48} className="invite-status-icon--success" />
            </div>
            <h2 className="invite-title">Du er inviteret til Workslip</h2>
            <p className="invite-text invite-text--long">
              Acceptér invitationen for at oprette din konto. Derefter beder vi om dit navn og telefonnummer, før du fortsætter med Microsoft.
            </p>
            <button
              type="button"
              className="btn btn-primary invite-btn invite-btn-accept"
              onClick={handleAcceptInvite}
            >
              Acceptér invitation
              <ArrowRight size={18} />
            </button>
          </>
        )}

        {state.status === 'success' ? (
          <>
            <div className="invite-status-icon">
              <CheckCircle2 size={48} className="invite-status-icon--success" />
            </div>
            <h2 className="invite-title">Velkommen til Workslip</h2>
            <p className="invite-text invite-text--long">
              Din konto er blevet oprettet.
            </p>
            <a
              href="/app/profil"
              className="btn btn-primary invite-btn"
            >
              Gå til profil
              <ArrowRight size={18} />
            </a>
          </>
        ) : null}

        {showDetailsForm && (
          <>
            <h2 className="invite-title">Færdiggør din konto</h2>
            <p className="invite-text invite-text--long">
              Indtast dit fulde navn og telefonnummer. Bagefter sender vi dig til Microsoft for login og passkey-oprettelse.
            </p>

            {state.status === 'error' && (
              <div className="invite-error-badge">
                <AlertTriangle size={16} />
                <span>{state.message}</span>
              </div>
            )}

            <form onSubmit={handleContinueToMicrosoft} className="invite-form">
              <div className="invite-field">
                <label htmlFor="displayName" className="invite-label">
                  Fulde navn
                </label>
                <div className="invite-input-wrapper">
                  <User size={16} className="invite-input-icon" />
                  <input
                    id="displayName"
                    type="text"
                    className="invite-input"
                    value={displayName}
                    onChange={e => setDisplayName(e.target.value)}
                    placeholder="Dit fulde navn"
                    required
                    disabled={isWorking}
                  />
                </div>
              </div>

              <div className="invite-field">
                <label htmlFor="phone" className="invite-label">
                  Telefonnummer <span className="text-dim">(valgfrit)</span>
                </label>
                <div className="invite-input-wrapper">
                  <Phone size={16} className="invite-input-icon" />
                  <input
                    id="phone"
                    type="tel"
                    className="invite-input"
                    value={phone}
                    onChange={e => setPhone(e.target.value)}
                    placeholder="+45 12 34 56 78"
                    disabled={isWorking}
                  />
                </div>
              </div>

              <button
                type="submit"
                className="btn btn-primary invite-btn invite-btn-submit"
                disabled={isWorking || !displayName.trim()}
                style={{
                  opacity: isWorking || !displayName.trim() ? 0.7 : 1,
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

            <div className="invite-footer">
              <Link to="/login" style={{ color: 'var(--text-muted)' }}>
                Har du allerede en konto? Log ind her
              </Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
};
