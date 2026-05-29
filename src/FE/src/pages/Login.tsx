import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { LogIn } from 'lucide-react';

export default function Login() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const navigate = useNavigate();

  const handleLogin = (e: React.FormEvent) => {
    e.preventDefault();
    // In a real app, perform auth here.
    // We just navigate to the app for now.
    navigate('/app');
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

        <form onSubmit={handleLogin} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
          <div className="form-group">
            <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 500 }}>Email</label>
            <input 
              type="email" 
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="montor@vvs.dk"
              required
              className="form-input"
            />
          </div>
          <div className="form-group">
            <label style={{ display: 'block', marginBottom: '0.5rem', fontSize: '0.9rem', fontWeight: 500 }}>Adgangskode</label>
            <input 
              type="password" 
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              required
              className="form-input"
            />
          </div>
          
          <button type="submit" className="btn btn-primary" style={{ width: '100%', marginTop: '0.5rem' }}>
            <LogIn size={18} style={{ marginRight: '0.5rem' }} />
            Log ind
          </button>
        </form>

        <div style={{ textAlign: 'center', marginTop: '2rem', fontSize: '0.9rem' }}>
          <Link to="/" style={{ color: 'var(--text-secondary)' }}>← Tilbage til forsiden</Link>
        </div>
      </div>
    </div>
  );
}
