import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { Navigate } from 'react-router-dom';
import { Loader2, ShieldCheck } from 'lucide-react';
import { FullscreenSystemState } from '../../../components/common/FullscreenSystemState';
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
  hasEntraLoginSession,
  InteractiveLoginRequiredError,
  LoginCancelledError,
  sanitizeReturnTo,
  startEntraLogin,
} from '../api/entraLogin';

const OneTimeCodeLogin = lazy(() =>
  import('../components/OneTimeCodeLogin').then((module) => ({ default: module.OneTimeCodeLogin })),
);

const LOGIN_INTERRUPTED_MESSAGE = 'Login afbrudt. Klik på knappen for at prøve igen.';
const devLoginEnabled = import.meta.env.DEV;

const isBackForwardNavigation = () =>
  typeof performance !== 'undefined' &&
  typeof performance.getEntriesByType === 'function' &&
  (performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined)?.type === 'back_forward';

export const Login = () => {
  const { isAuthenticated } = useAuth();
  const [historyInterruptedLogin] = useState(() =>
    !hasEntraLoginCallback() && hasEntraLoginSession() && isBackForwardNavigation(),
  );
  const [isSubmitting, setIsSubmitting] = useState(() => {
    const params = new URLSearchParams(window.location.search);
    return !historyInterruptedLogin && (hasEntraLoginCallback() || params.get('reauth') === '1');
  });
  const [errorMsg, setErrorMsg] = useState<string | null>(
    historyInterruptedLogin ? LOGIN_INTERRUPTED_MESSAGE : null,
  );
  const [showOtcLogin, setShowOtcLogin] = useState(false);
  const [isReauth, setIsReauth] = useState(
    () => !historyInterruptedLogin && new URLSearchParams(window.location.search).get('reauth') === '1',
  );
  const reauthStartedRef = useRef(false);

  const recoverToLogin = useCallback((message: string) => {
    clearReauthInFlight();
    clearEntraLoginSession();
    reauthStartedRef.current = false;
    window.history.replaceState(null, '', '/login');
    setErrorMsg(message);
    setIsSubmitting(false);
    setIsReauth(false);
  }, []);

  useEffect(() => {
    const handlePageShow = (event: PageTransitionEvent) => {
      if (!event.persisted || hasEntraLoginCallback() || !hasEntraLoginSession()) return;
      recoverToLogin(LOGIN_INTERRUPTED_MESSAGE);
    };

    window.addEventListener('pageshow', handlePageShow);
    return () => window.removeEventListener('pageshow', handlePageShow);
  }, [recoverToLogin]);

  useEffect(() => {
    if (reauthStartedRef.current) return;
    const params = new URLSearchParams(window.location.search);
    const isCallback = hasEntraLoginCallback();
    const isReauthRequest = params.get('reauth') === '1';
    if (historyInterruptedLogin) {
      clearReauthInFlight();
      clearEntraLoginSession();
      window.history.replaceState(null, '', '/login');
      return;
    }
    if (!isCallback && !isReauthRequest) return;
    reauthStartedRef.current = true;

    const returnTo = sanitizeReturnTo(params.get('returnTo'));
    const loginHint = AuthStorage.getItem(USER_EMAIL_KEY)?.trim() || undefined;
    const startInteractiveLogin = () => startEntraLogin({
      returnTo,
      prompt: 'select_account',
      ...(loginHint ? { loginHint } : {}),
    });

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
            startInteractiveLogin().catch(() => {
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

    // Silent reauth is useful only when Microsoft knows which account to try.
    // Without a login hint, `prompt=none` commonly bounces back with
    // interaction_required/account_selection_required and creates an avoidable
    // second Microsoft roundtrip. Go interactive immediately in that case.
    if (!loginHint) {
      startInteractiveLogin().catch(() => {
        recoverToLogin('Sessionen udløb. Log ind med passkey for at fortsætte.');
      });
      return;
    }

    startEntraLogin({ returnTo, prompt: 'none', loginHint }).catch((err: unknown) => {
      if (err instanceof InteractiveLoginRequiredError) {
        clearReauthInFlight();
        startInteractiveLogin().catch(() => {
          recoverToLogin('Sessionen udløb. Log ind med passkey for at fortsætte.');
        });
        return;
      }
      recoverToLogin('Sessionen udløb. Log ind med passkey for at fortsætte.');
    });
  }, [historyInterruptedLogin, recoverToLogin]);

  const handleMicrosoftLogin = async () => {
    setErrorMsg(null);
    setIsSubmitting(true);
    try {
      const returnTo = sanitizeReturnTo(new URLSearchParams(window.location.search).get('returnTo'));
      await startEntraLogin({ returnTo });
    } catch (err: unknown) {
      clearEntraLoginSession();
      const message = (err as Error)?.message || 'Kunne ikke starte Microsoft login.';
      setErrorMsg(message);
      setIsSubmitting(false);
    }
  };

  const handleDevLogin = async (email: string, redirect: string) => {
    if (!devLoginEnabled) return;

    setErrorMsg(null);
    setIsSubmitting(true);

    try {
      const { getDevToken } = await import('../api/devLogin');
      const response = await getDevToken(email);
      AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);
      AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);
      clearReauthInFlight();
      window.location.replace(redirect);
    } catch {
      setErrorMsg(`Dev login failed - ${email} not found`);
      setIsSubmitting(false);
    }
  };

  if (isAuthenticated) {
    return <Navigate to="/app" replace />;
  }

  if (isReauth) {
    return (
      <FullscreenSystemState
        title="Genindlæser login"
        message="Vi genopretter din sikre session og sender dig videre automatisk."
        actions={(
          <button
            type="button"
            onClick={() => {
              clearReauthInFlight();
              clearEntraLoginSession();
              window.history.replaceState(null, '', '/login');
              setIsReauth(false);
              setIsSubmitting(false);
            }}
            className="system-state-link"
          >
            Annuller
          </button>
        )}
      />
    );
  }

  return (
    <div className="app-container app-container-center">
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1" />
        <div className="bg-glow bg-glow-2" />
      </div>

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

            {devLoginEnabled && (
              <div className="login-email-step" aria-label="Developer login">
                {[
                  { label: 'Dev Login · User', email: 'user@17v3ygzs.mailosaur.net', redirect: '/app' },
                  { label: 'Dev Login · Auditor', email: 'auditor@17v3ygzs.mailosaur.net', redirect: '/app/auditor' },
                  { label: 'Dev Login · Admin', email: 'admin@17v3ygzs.mailosaur.net', redirect: '/app' },
                  { label: 'Dev Login · Superadmin', email: 'superadmin@17v3ygzs.mailosaur.net', redirect: '/app' },
                ].map((entry) => (
                  <button
                    key={entry.email}
                    type="button"
                    onClick={() => void handleDevLogin(entry.email, entry.redirect)}
                    disabled={isSubmitting}
                    className="btn btn-secondary login-submit-btn"
                  >
                    {entry.label}
                  </button>
                ))}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
};
