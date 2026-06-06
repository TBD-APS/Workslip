import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';

export const LandingPage = () => {
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const handleScroll = () => {
      setScrolled(window.scrollY > 50);
    };
    window.addEventListener('scroll', handleScroll, { passive: true });
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  return (
    <div className="app-container">
      {/* Dynamic Background */}
      <div className="bg-glow-wrapper">
        <div className="bg-glow bg-glow-1"></div>
        <div className="bg-glow bg-glow-2"></div>
      </div>

      {/* Navigation */}
      <nav className={`navbar ${scrolled ? 'scrolled' : ''}`}>
        <div className="logo">
          <svg className="logo-icon" width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
          Workslip
        </div>
        <div className="nav-links">
          <a href="#features" className="nav-link">Funktioner</a>
          <a href="#workflow" className="nav-link">Arbejdsgang</a>
          <a href="#kls" className="nav-link">KLS & Audit</a>
        </div>
        <div className="nav-actions">
          <Link to="/login" className="btn btn-secondary" style={{marginRight: '1rem'}}>Log ind</Link>
          <button className="btn btn-primary">Start gratis</button>
        </div>
      </nav>

      {/* Hero Section */}
      <main>
        <section className="hero">
          <div className="hero-badge">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
              <path d="M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z" stroke="currentColor" strokeWidth="2"/>
              <path d="M8 12L11 15L16 9" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
            Klar til KLS-Audit
          </div>
          
          <h1>
            Den digitale <span className="text-gradient">arbejdsseddel</span> til VVS
          </h1>
          
          <p>
            Få arbejdssedlen væk fra papir og ind i et digitalt jobflow. 
            Montøren dokumenterer arbejdet on-the-go, og kontoret kan følge op uden at jagte manglende oplysninger.
          </p>
          
          <div className="hero-cta">
            <button className="btn btn-primary" style={{padding: '1rem 2rem', fontSize: '1.1rem'}}>
              Opret din virksomhed
            </button>
            <button className="btn btn-secondary" style={{padding: '1rem 2rem', fontSize: '1.1rem'}}>
              Se hvordan det virker
            </button>
          </div>
          
          <div className="hero-visuals">
            <img 
              src="https://images.unsplash.com/photo-1551288049-bebda4e38f71?auto=format&fit=crop&q=80&w=1200&h=600" 
              alt="Workslip Backoffice Dashboard" 
              className="dashboard-mockup"
              style={{ objectFit: 'cover', objectPosition: 'top' }}
            />
            <img 
              src="https://images.unsplash.com/photo-1512428559087-560fa5ceab42?auto=format&fit=crop&q=80&w=300&h=600" 
              alt="Workslip Mobile App" 
              className="mobile-mockup"
              style={{ objectFit: 'cover' }}
            />
          </div>
        </section>
        
        {/* Further sections trimmed for brevity, assuming standard landing page content continues */}
      </main>
    </div>
  );
};
