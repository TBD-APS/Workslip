import { useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { LogIn, AlertCircle } from 'lucide-react';
import { toast } from 'sonner';

const loginSchema = z.object({
  email: z.string().email({ message: 'Ugyldig email adresse' }),
  password: z.string().min(6, { message: 'Adgangskoden skal være mindst 6 tegn' })
});

type LoginFormValues = z.infer<typeof loginSchema>;

export const Login = () => {
  const navigate = useNavigate();
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
  });

  const onSubmit = async (data: LoginFormValues) => {
    try {
      // Simulate API call
      await new Promise(resolve => setTimeout(resolve, 1000));
      console.log('Logging in with:', data);
      toast.success('Velkommen tilbage!');
      navigate('/app');
    } catch (error) {
      toast.error('Forkert email eller adgangskode');
    }
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
        <div style={{ textAlign: 'center', marginBottom: '2rem' }}>
          <div className="logo" style={{ justifyContent: 'center', marginBottom: '1rem' }}>
            <svg className="logo-icon" width="32" height="32" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
              <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </div>
          <h2>Log ind på Workslip</h2>
          <p style={{ color: 'var(--text-secondary)', marginTop: '0.5rem' }}>Velkommen tilbage</p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
          <div className="form-group">
            <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 500 }}>Email</label>
            <input 
              {...register('email')}
              type="email" 
              placeholder="montor@vvs.dk"
              className={`form-input ${errors.email ? 'border-red-500' : ''}`}
              style={errors.email ? { borderColor: '#ef4444' } : {}}
            />
            {errors.email && (
              <span style={{ color: '#ef4444', fontSize: '0.8rem', display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                <AlertCircle size={12} /> {errors.email.message}
              </span>
            )}
          </div>
          
          <div className="form-group">
            <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 500 }}>Adgangskode</label>
            <input 
              {...register('password')}
              type="password" 
              placeholder="••••••••"
              className="form-input"
              style={errors.password ? { borderColor: '#ef4444' } : {}}
            />
            {errors.password && (
              <span style={{ color: '#ef4444', fontSize: '0.8rem', display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                <AlertCircle size={12} /> {errors.password.message}
              </span>
            )}
          </div>
          
          <button type="submit" className="btn btn-primary" disabled={isSubmitting} style={{ width: '100%', marginTop: '0.5rem', opacity: isSubmitting ? 0.7 : 1 }}>
            <LogIn size={18} style={{ marginRight: '0.5rem' }} />
            {isSubmitting ? 'Logger ind...' : 'Log ind'}
          </button>
        </form>

        <div style={{ textAlign: 'center', marginTop: '2rem', fontSize: '0.9rem' }}>
          <Link to="/" style={{ color: 'var(--text-secondary)' }}>← Tilbage til forsiden</Link>
        </div>
      </div>
    </div>
  );
};
