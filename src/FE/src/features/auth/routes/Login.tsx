import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { Loader2, ShieldCheck } from 'lucide-react';
import { useAuth } from '../../../providers/useAuth';
import {
  AUTH_TOKEN_KEY,
  USER_EMAIL_KEY,
  AuthStorage,
  clearReauthInFlight,
} from '../../../providers/authContextValue';
import {
  clearEntraLoginSession,
  completeEntraLogin,
  hasEntraLoginCallback,
  InteractiveLoginRequiredError,
  LoginCancelledError,
  sanitizeReturnTo,
  startEntraLogin,
} from '../api/entraLogin';

const OneTimeCodeLogin = lazy(() =>
  import('../components/OneTimeCodeLogin').then((module) => ({ default: module.OneTimeCodeLogin })),
);

export const Login = () => {
  const navigate = useNavigate();
  const { isAuthenticated, devLogin } = useAuth();
  const [isSubmitting, setIsSubmitting] = useState(() => {
    const params = new URLSearchParams(window.location.search);
    return hasEntraLoginCallback() || params.get('reauth') === '1';
  });
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [showOtcLogin, setShowOtcLogin] = useState(false);
  // True when we arrived via the silent-reauth redirect (axios interceptor
  // sent us here because the JWT expired). While true we hide the login form
  // entirely and show only a spinner, so the user never sees the Microsoft
  // / passkey buttons flash before being sent to Microsoft.
  const [isReauth, setIsReauth] = useState(
    () => new URLSearchParams(window.location.search).get('reauth') === '1',
  );
  // Guards against React.StrictMode's double-mount in dev (and against any
  // edge case where this effect runs twice). The first call navigates the
  // browser to Microsoft; the second must be a no-op to avoid generating
  // a second PKCE state that overwrites the first.
  const reauthStartedRef = useRef(false);

  // Combined callback + reauth effect.
  //
  // Splitting these into two effects races when Microsoft redirects back with
  // BOTH `?reauth=1` and `?code=`. Keeping them in one effect makes the
  // callback and fresh-reauth branches explicit.
  useEffect(() => {
    if (reauthStartedRef.current) return;
    const params = new URLSearchParams(window.location.search);
    const isCallback = hasEntraLoginCallback();
    const isReauthRequest = params.get('reauth') === '1';
    if (!isCallback && !isReauthRequest) return;
    reauthStartedRef.current = true;

    const returnTo = sanitizeReturnTo(params.get('returnTo'));
    const recoverToLogin = (message: string) => {
      clearReauthInFlight();
      clearEntraLoginSession();
      window.history.replaceState(null, '', '/login');
      setErrorMsg(message);
      setIsSubmitting(false);
      setIsReauth(false);
    };

    if (isCallback) {
      completeEntraLogin()
        .then((result) => {
          AuthStorage.setItem(AUTH_TOKEN_KEY, result.auth.token);
          AuthStorage.setItem(USER_EMAIL_KEY, result.auth.user.email);
          clearReauthInFlight();
          clearEntraLoginSession();
          window.history.replaceState(null, '', '/login');
          window.location.replace(result.returnTo);
        })
        .catch((err: unknown) => {
          if (err instanceof InteractiveLoginRequiredError) {
            clearReauthInFlight();
            clearEntraLoginSession();
            window.history.replaceState(null, '', '/login');
            startEntraLogin({ returnTo, prompt: 'select_account' }).catch(() => {
              recoverToLogin('Sessionen udløb. Log ind med passkey for at fortsætte.');
            });
            return;
          }
          if (err instanceof LoginCancelledError) {
            recoverToLogin('Login afbrudt. Klik på knappen for at prøve igen.');
            return;
          }
          const message = (err as Error)?.message || 'Microsoft login fejlede. Prøv engangskode hvis passkey ikke virker.';
          recoverToLogin(message);
        });
      return;
    }

    startEntraLogin({ returnTo, prompt: 'none' }).catch((err: unknown) => {
      if (err instanceof InteractiveLoginRequiredError) {
        clearReauthInFlight();
        startEntraLogin({ returnTo, prompt: 'select_account' }).catch(() => {
          recoverToLogin('Sessionen udløb. Log ind med passkey for at fortsætte.');
        });
        return;
      }
      recoverToLogin('Sessionen udløb. Log ind med passkey for at fortsætte.');
    });
  }, []);

  const handleMicrosoftLogin = async () => {
    setErrorMsg(null);
    setIsSubmitting(true);
    try {
      const returnTo = sanitizeReturnTo(new URLSearchParams(window.location.search).get('returnTo'));
      await startEntraLogin({ returnTo });
    } catch (err: unknown) {
      const message = (err as Error)?.message || 'Kunne ikke starte Microsoft login.';
      setErrorMsg(message);
      setIsSubmitting(false);
    }
  };

  if (isAuthenticated) {
    return <Navigate to="/app" replace />;
  }

  return (
    <div className="app-container app-container-center">
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1" />
        <div className="bg-glow bg-glow-2" />
      </div>

      {isReauth && (
        <div className="reauth-card">
          <Loader2 className="animate-spin" size={32} />
          <p>Genindlæser login...</p>
          <button
            type="button"
            onClick={() => {
              clearReauthInFlight();
              clearEntraLoginSession();
              window.history.replaceState(null, '', '/login');
              setIsReauth(false);
              setIsSubmitting(false);
            }}
            className="reauth-cancel-btn"
          >
            Annuller
          </button>
        </div>
      )}

      {!isReauth && (
        <div className="login-card">
          {showOtcLogin ? (
            <Suspense
              fallback={(
                <div className="login-email-step" role="status" aria-live="polite">
                  <Loader2 className="animate-spin" size={24} />
                  <span>Vent venligst.. Indlæser modul</span>
                </div>
              )}
            >
              <OneTimeCodeLogin onBack={() => setShowOtcLogin(false)} />
            </Suspense>
          ) : (
            <>
              <div className="login-card-header">
                <div className="logo logo-center">
                  <svg className="logo-icon" width="32" height="32" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                    <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                    <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                    <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </div>
                <h2>Log ind på Workslip</h2>
                <p>Log ind med Microsoft passkey. Brug kun engangskode hvis passkey ikke virker eller du har fået ny telefon.</p>
              </div>

              {errorMsg && (
                <div className="login-error-banner">
                  <Loader2 size={16} />
                  {errorMsg}
                </div>
              )}

              <div className="login-email-step">
                <button
                  type="button"
                  className="btn btn-primary login-submit-btn"
                  onClick={handleMicrosoftLogin}
                  disabled={isSubmitting}
                >
                  {isSubmitting ? <Loader2 className="animate-spin" size={18} /> : <ShieldCheck size={18} />}
                  <span>{isSubmitting ? 'Sender til Microsoft...' : 'Log ind med Microsoft passkey'}</span>
                </button>

                <button
                  type="button"
                  onClick={() => setShowOtcLogin(true)}
                  className="login-otc-btn"
                >
                  Mistet dit login? Modtag engangskode
                </button>
              </div>

              {import.meta.env.DEV && import.meta.env.VITE_ENABLE_DEV_LOGIN === 'true' && (
                <div className="login-dev-section">
                  <div className="login-dev-buttons">
                    {[
                      { label: 'Dev Login · User', email: 'user@17v3ygzs.mailosaur.net', redirect: '/app' },
                      { label: 'Dev Login · Auditor', email: 'auditor@17v3ygzs.mailosaur.net', redirect: '/app/auditor' },
                      { label: 'Dev Login · Admin', email: 'admin@17v3ygzs.mailosaur.net', redirect: '/app' },
                      { label: 'Dev Login · Superadmin', email: 'rasmusvm6@hotmail.com', redirect: '/app' },
                    ].map((entry) => (
                      <button
                        key={entry.email}
                        onClick={async () => {
                          setErrorMsg(null);
                          setIsSubmitting(true);
                          try {
                            const success = await devLogin(entry.email);
                            if (success) navigate(entry.redirect, { replace: true });
                            else setErrorMsg(`Dev login failed - ${entry.email} not found`);
                          } catch {
                            setErrorMsg('Dev login failed');
                          } finally {
                            setIsSubmitting(false);
                          }
                        }}
                        disabled={isSubmitting}
                        className="btn btn-secondary login-dev-btn"
                      >
                        {entry.label}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
};
