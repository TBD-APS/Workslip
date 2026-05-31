import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';

export default function LandingPage() {
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const handleScroll = () => {
      setScrolled(window.scrollY > 50);
    };
    window.addEventListener('scroll', handleScroll);
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

        {/* Features Section */}
        <section id="features" className="features">
          <div className="section-header">
            <h2>Hvorfor vælge Workslip?</h2>
            <p>Vi har bygget præcis de værktøjer, der gør din hverdag lettere og din dokumentation stærk.</p>
          </div>
          
          <div className="feature-grid">
            <div className="feature-card">
              <div className="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                  <path d="M12 18H12.01M8 21H16C17.1046 21 18 20.1046 18 19V5C18 3.89543 17.1046 3 16 3H8C6.89543 3 6 3.89543 6 5V19C6 20.1046 6.89543 21 8 21Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <h3>PWA til montøren</h3>
              <p>Hurtig adgang fra telefonen. Appen tvinger stillingtagen til relevante kontrolpunkter (4V05), så intet glemmes.</p>
            </div>
            
            <div className="feature-card">
              <div className="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                  <path d="M9 11L12 14L22 4M21 12V19C21 20.1046 20.1046 21 19 21H5C3.89543 21 3 20.1046 3 19V5C3 3.89543 3.89543 3 5 3H16" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <h3>Altid Auditklar</h3>
              <p>Revisionsspor gemmer væsentlige handlinger med bruger og tidspunkt. Nemt at fremvise dokumentation ved KLS-audit.</p>
            </div>
            
            <div className="feature-card">
              <div className="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                  <path d="M21 21L15 15M17 10C17 13.866 13.866 17 10 17C6.13401 17 3 13.866 3 10C3 6.13401 6.13401 3 10 3C13.866 3 17 6.13401 17 10Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <h3>Backoffice Overblik</h3>
              <p>Kontoret kan nemt se, hvilke jobs der mangler oplysninger, hvad der er returneret, og hvornår noget er fakturaklart.</p>
            </div>
          </div>
        </section>

        {/* Workflow Section */}
        <section id="workflow" className="workflow">
          <div className="workflow-container">
            <div className="workflow-content">
              <h2>Det simple jobflow</h2>
              <p>Fra første kontakt til afsluttet kontrol. Workslip binder kontoret og montøren sammen i én samlet proces.</p>
              
              <div className="workflow-steps">
                <div className="step">
                  <div className="step-number">1</div>
                  <div className="step-content">
                    <h4>Kontoret opretter</h4>
                    <p>Job oprettes og tildeles en montør med alle nødvendige kundeinformationer.</p>
                  </div>
                </div>
                
                <div className="step">
                  <div className="step-number">2</div>
                  <div className="step-content">
                    <h4>Montøren udfører</h4>
                    <p>Opgaven dokumenteres, kontrolpunkter udfyldes, og eventuelle afvigelser noteres.</p>
                  </div>
                </div>
                
                <div className="step">
                  <div className="step-number">3</div>
                  <div className="step-content">
                    <h4>Kontoret godkender</h4>
                    <p>Jobbet gennemgås i backoffice. Returneres ved mangler eller markeres som fakturaklart.</p>
                  </div>
                </div>
              </div>
            </div>
            
            <div className="workflow-image" style={{ position: 'relative' }}>
              <div style={{
                background: 'var(--surface-color)',
                border: '1px solid var(--surface-border)',
                borderRadius: '24px',
                padding: '2rem',
                boxShadow: '0 20px 40px rgba(0,0,0,0.4)',
                backdropFilter: 'blur(20px)'
              }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                </div>
              </div>
            </div>
          </div>
        </section>
      </main>

      {/* Footer */}
      <footer>
        <div className="footer-content">
          <div className="logo" style={{ fontSize: '1.25rem' }}>
            <svg className="logo-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
              <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
            Workslip
          </div>
          <div className="footer-text">
            © {new Date().getFullYear()} Workslip. Alle rettigheder forbeholdes.
          </div>
        </div>
      </footer>
    </div>
  );
}
