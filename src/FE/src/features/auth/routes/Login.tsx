import { useState, useRef, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { Mail, ArrowLeft, Loader2, ShieldCheck } from 'lucide-react';
import { useAuth } from '../../../providers/useAuth';
import { sendAuthCode } from '../api/devToken';
import { toast } from 'sonner';
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

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL;

const EmailSchema = z.object({
  email: z.string().email({ message: 'Ugyldig email adresse' }),
});

const CodeSchema = z.object({
  code: z.string().min(6, { message: 'Koden skal være 6 tegn' }),
});

type EmailFormValues = z.infer<typeof EmailSchema>;
type CodeFormValues = z.infer<typeof CodeSchema>;

export const Login = () => {
  const navigate = useNavigate();
  const { login, devLogin } = useAuth();
  const [step, setStep] = useState<'email' | 'code'>('email');
  const [email, setEmail] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [showOtcLogin, setShowOtcLogin] = useState(false);
  // True when we arrived via the silent-reauth redirect (axios interceptor
  // sent us here because the JWT expired). While true we hide the login form
  // entirely and show only a spinner, so the user never sees the Microsoft
  // / passkey buttons flash before being sent to Microsoft.
  const [isReauth, setIsReauth] = useState(
    () => new URLSearchParams(window.location.search).get('reauth') === '1',
  );
  const codeInputRef = useRef<HTMLInputElement>(null);
  // Guards against React.StrictMode's double-mount in dev (and against any
  // edge case where this effect runs twice). The first call navigates the
  // browser to Microsoft; the second must be a no-op to avoid generating
  // a second PKCE state that overwrites the first.
  const reauthStartedRef = useRef(false);

  const emailForm = useForm<EmailFormValues>({
    resolver: zodResolver(EmailSchema),
    defaultValues: {
      email: new URLSearchParams(window.location.search).get('email') || '',
    }
  });

  const codeForm = useForm<CodeFormValues>({
    resolver: zodResolver(CodeSchema),
  });
  const { ref: codeFieldRef, ...codeField } = codeForm.register('code');

  // Combined callback + reauth effect.
  //
  // Splitting these into two effects races when Microsoft redirects back with
  // BOTH `?reauth=1` and `?code=` — the second effect's guard
  // (`hasEntraLoginCallback() || params.get('reauth') !== '1'`) is implicit and
  // easy to break. Keeping them in one effect makes the branches explicit.
  useEffect(() => {
    if (reauthStartedRef.current) return;
    const params = new URLSearchParams(window.location.search);
    const isCallback = hasEntraLoginCallback();
    const isReauth = params.get('reauth') === '1';
    if (!isCallback && !isReauth) return;
    reauthStartedRef.current = true;

    const returnTo = sanitizeReturnTo(params.get('returnTo'));

    if (isCallback) {
      setIsSubmitting(true);
      completeEntraLogin()
        .then(result => {
          AuthStorage.setItem(AUTH_TOKEN_KEY, result.auth.token);
          AuthStorage.setItem(USER_EMAIL_KEY, result.auth.user.email);
          clearReauthInFlight();
          clearEntraLoginSession();
          window.history.replaceState(null, '', '/login');
          window.location.assign(result.returnTo);
        })
        .catch((err: unknown) => {
          if (err instanceof InteractiveLoginRequiredError) {
            // Microsoft blocked the silent flow even after we already
            // auto-escalated from prompt=none. Force interactive login.
            clearReauthInFlight();
            clearEntraLoginSession();
            window.history.replaceState(null, '', '/login');
            startEntraLogin({ returnTo, prompt: 'select_account' }).catch(() => {
              setErrorMsg('Sessionen udløb. Log ind med passkey for at fortsætte.');
              setIsSubmitting(false);
            });
            return;
          }
          if (err instanceof LoginCancelledError) {
            // User clicked "Cancel" / "Tilbage" in the Microsoft dialog.
            // Clean up state and let them try again on their own terms —
            // do NOT auto-escalate, since the choice to cancel was deliberate.
            clearReauthInFlight();
            clearEntraLoginSession();
            window.history.replaceState(null, '', '/login');
            setErrorMsg('Login afbrudt. Klik på knappen for at prøve igen.');
            setIsSubmitting(false);
            setIsReauth(false);
            return;
          }
          window.history.replaceState(null, '', '/login');
          clearEntraLoginSession();
          const message = (err as Error)?.message || 'Microsoft login fejlede. Prøv engangskode hvis passkey ikke virker.';
          setErrorMsg(message);
          toast.error(message);
          setIsSubmitting(false);
        });
      return;
    }

    // Fresh reauth: try silent first, escalate on InteractiveLoginRequiredError.
    setIsSubmitting(true);
    startEntraLogin({ returnTo, prompt: 'none' }).catch((err: unknown) => {
      if (err instanceof InteractiveLoginRequiredError) {
        clearReauthInFlight();
        startEntraLogin({ returnTo, prompt: 'select_account' }).catch(() => {
          setErrorMsg('Sessionen udløb. Log ind med passkey for at fortsætte.');
          setIsSubmitting(false);
        });
        return;
      }
      setErrorMsg('Sessionen udløb. Log ind med passkey for at fortsætte.');
      setIsSubmitting(false);
      setIsReauth(false);
    });
  }, []);

  useEffect(() => {
    if (step !== 'code' || !codeInputRef.current) return undefined;

    const focusTimer = setTimeout(() => codeInputRef.current?.focus(), 50);
    return () => clearTimeout(focusTimer);
  }, [step]);

  const handleSendCode = async (data: EmailFormValues) => {
    setErrorMsg(null);
    setIsSubmitting(true);
    try {
      await sendAuthCode(data.email);
      setEmail(data.email);
      setStep('code');
      toast.success('Tjek din indbakke – en kode er sendt.');
    } catch {
      toast.error('Kunne ikke sende kode. Prøv igen.');
      setErrorMsg('Kunne ikke sende kode. Prøv igen.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleMicrosoftLogin = async () => {
    setErrorMsg(null);
    setIsSubmitting(true);
    try {
      const returnTo = sanitizeReturnTo(new URLSearchParams(window.location.search).get('returnTo'));
      await startEntraLogin({ returnTo });
    } catch (err: unknown) {
      const message = (err as Error)?.message || 'Kunne ikke starte Microsoft login.';
      setErrorMsg(message);
      toast.error(message);
      setIsSubmitting(false);
    }
  };

  const handleVerifyCode = async (data: CodeFormValues) => {
    setErrorMsg(null);
    setIsSubmitting(true);
    try {
      const success = await login(email, data.code);
      if (success) {
        clearReauthInFlight();
        navigate('/app');
      } else {
        setErrorMsg('Ugyldig kode. Prøv igen.');
        toast.error('Ugyldig kode. Prøv igen.');
      }
    } catch {
      setErrorMsg('Ugyldig kode. Prøv igen.');
      toast.error('Ugyldig kode. Prøv igen.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1" />
        <div className="bg-glow bg-glow-2" />
      </div>

      {isReauth && (
        <div style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: '1rem',
          padding: '2rem',
          background: 'var(--surface-color)',
          border: '1px solid var(--surface-border)',
          borderRadius: '24px',
          backdropFilter: 'blur(20px)',
          boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5)',
          maxWidth: '400px',
          width: '100%',
        }}>
          <Loader2 className="animate-spin" size={32} style={{ color: 'var(--text-secondary)' }} />
          <p style={{ color: 'var(--text-secondary)', textAlign: 'center', margin: 0 }}>
            Genindlæser login...
          </p>
          <button
            type="button"
            onClick={() => {
              clearReauthInFlight();
              clearEntraLoginSession();
              window.history.replaceState(null, '', '/login');
              setIsReauth(false);
              setIsSubmitting(false);
            }}
            style={{
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              color: 'var(--text-secondary)',
              fontSize: '0.9rem',
              padding: '0.5rem',
              textDecoration: 'underline',
            }}
          >
            Annuller
          </button>
        </div>
      )}

      {!isReauth && (
      <div style={{
        width: '100%',
        maxWidth: '400px',
        padding: '2rem',
        background: 'var(--surface-color)',
        border: '1px solid var(--surface-border)',
        borderRadius: '24px',
        backdropFilter: 'blur(20px)',
        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5)'
      }}>
        <div style={{ textAlign: 'center', marginBottom: '2rem' }}>
          <div className="logo" style={{ justifyContent: 'center', marginBottom: '1rem' }}>
            <svg className="logo-icon" width="32" height="32" viewBox="0 0 24 24" fill="none">
              <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </div>
          <h2 style={{ marginBottom: '0.5rem' }}>Log ind på Workslip</h2>
          {step === 'email' && (
            <p style={{ color: 'var(--text-secondary)' }}>Log ind med Microsoft passkey. Brug kun engangskode hvis passkey ikke virker eller du har fået ny telefon.</p>
          )}
          {step === 'code' && (
            <div>
              <p style={{ color: 'var(--text-secondary)' }}>En kode er sendt til</p>
              <p style={{ fontWeight: 400, fontSize: '1.1rem', marginTop: '0.25rem' }}>{email}</p>
            </div>
          )}
        </div>

        {errorMsg && (
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
            gap: '0.5rem'
          }}>
            <Loader2 size={16} />
            {errorMsg}
          </div>
        )}

        {step === 'email' && !showOtcLogin && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <button
              type="button"
              className="btn btn-primary"
              onClick={handleMicrosoftLogin}
              disabled={isSubmitting}
              style={{
                width: '100%',
                opacity: isSubmitting ? 0.7 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem',
                cursor: isSubmitting ? 'wait' : 'pointer'
              }}
            >
              {isSubmitting ? <Loader2 className="animate-spin" size={18} /> : <ShieldCheck size={18} />}
              <span>{isSubmitting ? 'Sender til Microsoft...' : 'Log ind med Microsoft passkey'}</span>
            </button>

            <button
              type="button"
              onClick={() => setShowOtcLogin(true)}
              style={{
                width: '100%',
                background: 'none',
                border: 'none',
                cursor: 'pointer',
                color: 'var(--text-secondary)',
                fontSize: '0.9rem',
                padding: '0.75rem'
              }}
            >
              Passkey virker ikke / ny telefon? Brug engangskode
            </button>
          </div>
        )}

        {step === 'email' && showOtcLogin && (
          <form onSubmit={emailForm.handleSubmit(handleSendCode)} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            <div className="form-group">
              <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 400 }}>Email</label>
              <input
                {...emailForm.register('email')}
                type="email"
                placeholder="din@email.dk"
                className="form-input"
                autoComplete="email"
                style={emailForm.formState.errors.email ? { borderColor: '#ef4444' } : {}}
              />
              {emailForm.formState.errors.email && (
                <span style={{ color: '#ef4444', fontSize: '0.8rem', marginTop: '4px' }}>
                  {emailForm.formState.errors.email.message}
                </span>
              )}
            </div>

            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSubmitting}
              style={{
                width: '100%',
                opacity: isSubmitting ? 0.7 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem',
                position: 'relative',
                overflow: 'hidden',
                cursor: isSubmitting ? 'wait' : 'pointer'
              }}
            >
              {isSubmitting && (
                <span style={{
                  position: 'absolute',
                  inset: 0,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  background: 'inherit',
                  backdropFilter: 'blur(2px)'
                }}>
                  <Loader2 className="animate-spin" size={18} />
                </span>
              )}
              <Mail size={18} />
              <span>{isSubmitting ? 'Sender kode...' : 'Send kode'}</span>
            </button>
          </form>
        )}

        {step === 'code' && (
          <form onSubmit={codeForm.handleSubmit(handleVerifyCode)} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            <div className="form-group">
              <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 400 }}>Engangskode</label>
              <input
                {...codeField}
                ref={(element) => {
                  codeFieldRef(element);
                  codeInputRef.current = element;
                }}
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                placeholder="123456"
                className="form-input"
                maxLength={6}
                autoComplete="one-time-code"
                style={codeForm.formState.errors.code ? { borderColor: '#ef4444' } : {}}
              />
              {codeForm.formState.errors.code && (
                <span style={{ color: '#ef4444', fontSize: '0.8rem', marginTop: '4px' }}>
                  {codeForm.formState.errors.code.message}
                </span>
              )}
            </div>

            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSubmitting}
              style={{
                width: '100%',
                opacity: isSubmitting ? 0.7 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem'
              }}
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/><polyline points="10 17 15 12 10 7"/><line x1="15" y1="12" x2="3" y2="12"/></svg>
              <span>Log ind</span>
            </button>

            <button
              type="button"
              onClick={() => {
                setErrorMsg(null);
                codeForm.reset();
                setStep('email');
              }}
              style={{
                width: '100%',
                background: 'none',
                border: 'none',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem',
                color: 'var(--text-secondary)',
                fontSize: '0.9rem',
                padding: '0.75rem'
              }}
            >
              <ArrowLeft size={16} />
              Tilbage
            </button>
          </form>
        )}

        <div style={{ textAlign: 'center', marginTop: '1.5rem', fontSize: '0.9rem' }}>
          <Link to="/" style={{ color: 'var(--text-secondary)' }}>← Tilbage til forsiden</Link>
        </div>

        {apiBaseUrl && (
          <div style={{ marginTop: '2rem', paddingTop: '1.5rem', borderTop: '1px solid var(--surface-border)' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
              {[
                { label: 'Dev Login · User', email: 'user@17v3ygzs.mailosaur.net' },
                { label: 'Dev Login · Admin', email: 'admin@17v3ygzs.mailosaur.net' },
                { label: 'Dev Login · SuperAdmin', email: 'rbj@17v3ygzs.mailosaur.net' },
              ].map((entry) => (
                <button
                  key={entry.email}
                  onClick={async () => {
                    setErrorMsg(null);
                    setIsSubmitting(true);
                    try {
                      const success = await devLogin(entry.email);
                      if (success) navigate('/app');
                      else setErrorMsg(`Dev login failed - ${entry.email} not found`);
                    } catch {
                      setErrorMsg('Dev login failed');
                    } finally {
                      setIsSubmitting(false);
                    }
                  }}
                  disabled={isSubmitting}
                  className="btn btn-secondary"
                  style={{ width: '100%', fontSize: '0.85rem' }}
                >
                  {entry.label}
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
      )}
    </div>
  );
};
