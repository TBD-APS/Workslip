import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { LogIn, AlertCircle, Mail, Loader2, ArrowLeft } from 'lucide-react';
import { useAuth } from '../../../providers/AuthContext';
import { sendAuthCode } from '../api/devToken';

const EmailSchema = z.object({
  email: z.string().email({ message: 'Ugyldig email adresse' }),
});

const CodeSchema = z.object({
  code: z.string().min(6, { message: 'Koden skal være 6 tegn' }).max(6, { message: 'Koden skal være 6 tegn' }),
});

type EmailFormValues = z.infer<typeof EmailSchema>;
type CodeFormValues = z.infer<typeof CodeSchema>;

type Step = 'email' | 'code' | 'verified';

export const Login = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [step, setStep] = useState<Step>('email');
  const [email, setEmail] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [isVerifying, setIsVerifying] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const emailForm = useForm<EmailFormValues>({
    resolver: zodResolver(EmailSchema),
  });

  const codeForm = useForm<CodeFormValues>({
    resolver: zodResolver(CodeSchema),
  });

  const onSendCode = async (data: EmailFormValues) => {
    setErrorMsg(null);
    setIsSending(true);
    try {
      await sendAuthCode(data.email);
      setEmail(data.email);
      setStep('code');
    } catch {
      setErrorMsg('Kunne ikke sende kode. Prøv igen.');
    } finally {
      setIsSending(false);
    }
  };

  const onVerifyCode = async (data: CodeFormValues) => {
    setErrorMsg(null);
    setIsVerifying(true);
    try {
      const success = await login(email, data.code);
      if (success) {
        navigate('/app');
      } else {
        setErrorMsg('Forkert kode. Prøv igen.');
      }
    } catch {
      setErrorMsg('Noget gik galt. Prøv igen.');
    } finally {
      setIsVerifying(false);
    }
  };

  const goBack = () => {
    setErrorMsg(null);
    setStep('email');
  };

  return (
    <div className="app-container" style={{ justifyContent: 'center', alignItems: 'center' }}>
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1"></div>
        <div className="bg-glow bg-glow-2"></div>
      </div>

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
        <div style={{ textAlign: 'center', marginBottom: step === 'verified' ? '1rem' : '2rem' }}>
          <div className="logo" style={{ justifyContent: 'center', marginBottom: '1rem' }}>
            <svg className="logo-icon" width="32" height="32" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
              <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </div>
          {step === 'email' && (
            <>
              <h2>Log ind på Workslip</h2>
              <p style={{ color: 'var(--text-secondary)', marginTop: '0.5rem' }}>Indtast din email for at logge ind</p>
            </>
          )}
          {step === 'code' && (
            <>
              <div style={{ marginBottom: '0.5rem' }}>
                <Mail size={32} style={{ margin: '0 auto', display: 'block', color: 'var(--text-secondary)' }} />
              </div>
              <h2>Tjek din indbakke</h2>
              <p style={{ color: 'var(--text-secondary)', marginTop: '0.5rem', fontSize: '0.9rem' }}>
                En kode er sendt til <strong>{email}</strong>
              </p>
            </>
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
            <AlertCircle size={16} />
            {errorMsg}
          </div>
        )}

        {step === 'email' && (
          <form onSubmit={emailForm.handleSubmit(onSendCode)} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            <div className="form-group">
              <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 500 }}>Email</label>
              <input 
                {...emailForm.register('email')}
                type="email" 
                placeholder="dit@email.dk"
                className="form-input"
                style={emailForm.formState.errors.email ? { borderColor: '#ef4444' } : {}}
              />
              {emailForm.formState.errors.email && (
                <span style={{ color: '#ef4444', fontSize: '0.8rem', display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                  <AlertCircle size={12} /> {emailForm.formState.errors.email.message}
                </span>
              )}
            </div>
            
            <button 
              type="submit" 
              className="btn btn-primary" 
              disabled={isSending} 
              style={{ 
                width: '100%', 
                marginTop: '0.5rem', 
                opacity: isSending ? 0.7 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem'
              }}
            >
              {isSending ? <Loader2 size={18} className="animate-spin" /> : <Mail size={18} />}
              {isSending ? 'Sender kode...' : 'Send kod'}
            </button>
          </form>
        )}

        {step === 'code' && (
          <form onSubmit={codeForm.handleSubmit(onVerifyCode)} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            <div className="form-group">
              <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 500 }}>En gang kode</label>
              <input 
                {...codeForm.register('code')}
                type="text" 
                placeholder="123456"
                className="form-input"
                style={codeForm.formState.errors.code ? { borderColor: '#ef4444' } : {}}
                autoComplete="one-time-code"
              />
              {codeForm.formState.errors.code && (
                <span style={{ color: '#ef4444', fontSize: '0.8rem', display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                  <AlertCircle size={12} /> {codeForm.formState.errors.code.message}
                </span>
              )}
            </div>
            
            <button 
              type="submit" 
              className="btn btn-primary" 
              disabled={isVerifying} 
              style={{ 
                width: '100%', 
                marginTop: '0.5rem', 
                opacity: isVerifying ? 0.7 : 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem'
              }}
            >
              {isVerifying ? <Loader2 size={18} className="animate-spin" /> : <LogIn size={18} />}
              {isVerifying ? 'Logger ind...' : 'Log ind'}
            </button>

            <button 
              type="button"
              onClick={goBack}
              style={{ 
                width: '100%', 
                marginTop: '0.5rem',
                background: 'none',
                border: '1px solid var(--surface-border)',
                borderRadius: '8px',
                padding: '0.75rem',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem',
                color: 'var(--text-secondary)'
              }}
            >
              <ArrowLeft size={16} />
              Tilbage
            </button>
          </form>
        )}

        <div style={{ textAlign: 'center', marginTop: step === 'email' ? '2rem' : '1.5rem', fontSize: '0.9rem' }}>
          <Link to="/" style={{ color: 'var(--text-secondary)' }}>← Tilbage til forsiden</Link>
        </div>
      </div>
    </div>
  );
};
