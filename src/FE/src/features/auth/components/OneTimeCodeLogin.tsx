import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { ArrowLeft, Loader2, Mail } from 'lucide-react';
import { notify } from '../../../lib/toast';
import { clearReauthInFlight } from '../../../providers/authContextValue';
import { useAuth } from '../../../providers/useAuth';
import { sendAuthCode } from '../api/devToken';
import { OneTimeCodeInput } from './OneTimeCodeInput';

const EmailSchema = z.object({
  email: z.string().email({ message: 'Ugyldig email adresse' }),
});

const CodeSchema = z.object({
  code: z.string().regex(/^\d{6}$/, { message: 'Koden skal bestå af 6 cifre' }),
});

type EmailFormValues = z.infer<typeof EmailSchema>;
type CodeFormValues = z.infer<typeof CodeSchema>;

interface OneTimeCodeLoginProps {
  onBack: () => void;
}

export function OneTimeCodeLogin({ onBack }: OneTimeCodeLoginProps) {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [step, setStep] = useState<'email' | 'code'>('email');
  const [email, setEmail] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const codeInputRef = useRef<HTMLInputElement>(null);

  const emailForm = useForm<EmailFormValues>({
    resolver: zodResolver(EmailSchema),
    defaultValues: {
      email: new URLSearchParams(window.location.search).get('email') || '',
    },
  });

  const codeForm = useForm<CodeFormValues>({
    resolver: zodResolver(CodeSchema),
    defaultValues: {
      code: '',
    },
  });

  useEffect(() => {
    if (step !== 'code' || !codeInputRef.current) return undefined;

    const focusTimer = window.setTimeout(() => codeInputRef.current?.focus(), 50);
    return () => window.clearTimeout(focusTimer);
  }, [step]);

  const handleSendCode = async (data: EmailFormValues) => {
    setErrorMsg(null);
    setIsSubmitting(true);
    try {
      await sendAuthCode(data.email);
      setEmail(data.email);
      setStep('code');
      notify.success('Tjek din indbakke – en kode er sendt.');
    } catch {
      notify.error('Kunne ikke sende kode. Prøv igen.');
      setErrorMsg('Kunne ikke sende kode. Prøv igen.');
    } finally {
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
        notify.error('Ugyldig kode. Prøv igen.');
      }
    } catch {
      setErrorMsg('Ugyldig kode. Prøv igen.');
      notify.error('Ugyldig kode. Prøv igen.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
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
        {step === 'email' ? (
          <p>Indtast din email for at modtage en engangskode.</p>
        ) : (
          <div>
            <p>En kode er sendt til</p>
            <p className="login-email-info">{email}</p>
          </div>
        )}
      </div>

      {errorMsg && (
        <div className="login-error-banner">
          <Loader2 size={16} />
          {errorMsg}
        </div>
      )}

      {step === 'email' ? (
        <form onSubmit={emailForm.handleSubmit(handleSendCode)} className="login-form">
          <div className="form-group">
            <label htmlFor="otc-email">Email</label>
            <input
              {...emailForm.register('email')}
              id="otc-email"
              type="email"
              placeholder="din@email.dk"
              className={`form-input${emailForm.formState.errors.email ? ' form-input-invalid' : ''}`}
              autoComplete="email"
            />
            {emailForm.formState.errors.email && (
              <span className="form-error-text">
                {emailForm.formState.errors.email.message}
              </span>
            )}
          </div>

          <button
            type="submit"
            className="btn btn-primary login-submit-btn"
            disabled={isSubmitting}
          >
            {isSubmitting && (
              <span className="login-submit-btn-overlay">
                <Loader2 className="animate-spin" size={18} />
              </span>
            )}
            <Mail size={18} />
            <span>{isSubmitting ? 'Sender kode...' : 'Send kode'}</span>
          </button>

          <button type="button" onClick={onBack} className="login-back-btn">
            <ArrowLeft size={16} />
            Tilbage til passkey login
          </button>
        </form>
      ) : (
        <form onSubmit={codeForm.handleSubmit(handleVerifyCode)} className="login-form">
          <div className="form-group">
            <label htmlFor="otc-code">Engangskode</label>
            <Controller
              name="code"
              control={codeForm.control}
              render={({ field }) => (
                <OneTimeCodeInput
                  id="otc-code"
                  name={field.name}
                  ref={(element) => {
                    field.ref(element);
                    codeInputRef.current = element;
                  }}
                  value={field.value}
                  onValueChange={field.onChange}
                  onBlur={field.onBlur}
                  hasError={Boolean(codeForm.formState.errors.code)}
                  aria-invalid={Boolean(codeForm.formState.errors.code)}
                  aria-describedby={codeForm.formState.errors.code ? 'otc-code-help otc-code-error' : 'otc-code-help'}
                  disabled={isSubmitting}
                />
              )}
            />
            <p id="otc-code-help" className="login-code-help">
              Kan du ikke finde mailen? Tjek spam eller uønsket post.
            </p>
            {codeForm.formState.errors.code && (
              <span id="otc-code-error" className="form-error-text">
                {codeForm.formState.errors.code.message}
              </span>
            )}
          </div>

          <button
            type="submit"
            className="btn btn-primary login-submit-btn"
            disabled={isSubmitting}
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4" />
              <polyline points="10 17 15 12 10 7" />
              <line x1="15" y1="12" x2="3" y2="12" />
            </svg>
            <span>Log ind</span>
          </button>

          <button
            type="button"
            onClick={() => {
              setErrorMsg(null);
              codeForm.reset();
              setStep('email');
              onBack();
            }}
            className="login-back-btn"
          >
            <ArrowLeft size={16} />
            Tilbage til login
          </button>
        </form>
      )}
    </>
  );
}
