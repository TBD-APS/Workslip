import { useNavigate, NavLink, Outlet } from 'react-router-dom';
import { ClipboardList, Building2, CalendarDays, LogOut, PlusCircle, Settings, User, Users } from 'lucide-react';
import { useAuth } from '../../providers/useAuth';
import { Can } from '../../providers/permissions';
import { useEffect, useState } from 'react';
import { DropdownProvider } from '../../providers/DropdownContext';

export const AppLayout = () => {
  const navigate = useNavigate();
  const { logout, user } = useAuth();
  const [isKeyboardVisible, setIsKeyboardVisible] = useState(false);

  const handleLogout = () => {
    logout();
    // Navigate immediately rather than waiting for ProtectedRoute to render
    // a <Navigate to="/login"> — avoids a single frame of protected content
    // still being visible after the user clicked logout, and prevents a
    // browser-back race where the protected URL is briefly visible again.
    navigate('/login', { replace: true });
  };

  useEffect(() => {
    const handleFocusChange = () => {
      const activeElement = document.activeElement;
      const isInput = activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement;
      setIsKeyboardVisible(isInput);
    };

    document.addEventListener('focusin', handleFocusChange);
    document.addEventListener('focusout', () => {
      // Small timeout to allow next element to focus before hiding
      setTimeout(handleFocusChange, 50);
    });

    return () => {
      document.removeEventListener('focusin', handleFocusChange);
      document.removeEventListener('focusout', handleFocusChange);
    };
  }, []);

  return (
    <DropdownProvider>
      <div className="app-shell">
        {/* Top Header for Mobile */}
      <header className="app-header">
        <button className="logo" style={{ fontSize: '1.25rem' }} onClick={() => navigate('/app')}>
          <svg className="logo-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
          Workslip
        </button>
        <div className="app-header-actions">
          <span className="app-header-user" title={user?.email ?? ''}>
            <User size={16} />
            <span>{user?.displayName ?? user?.email ?? ''}</span>
          </span>
          <Can permission="user:manage">
            <button
              type="button"
              onClick={() => navigate('/app/settings')}
              className="user-avatar"
              aria-label="Indstillinger"
              title="Indstillinger"
            >
              <Settings size={18} />
            </button>
          </Can>
          <button
            type="button"
            onClick={() => navigate('/app/profil')}
            className="user-avatar"
            aria-label="Profil"
            title="Profil"
          >
            {user?.displayName ? (
              <span style={{ fontSize: '0.9rem', fontWeight: 500 }}>
                {user.displayName.charAt(0).toUpperCase()}
              </span>
            ) : (
              <User size={18} />
            )}
          </button>
          <button
            type="button"
            onClick={handleLogout}
            className="app-header-logout"
            aria-label="Log ud"
            title="Log ud"
          >
            <LogOut size={18} />
            <span>Log ud</span>
          </button>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="app-content">
        <Outlet />
      </main>

      {/* Bottom Navigation (Mobile First) */}
      <nav className={`bottom-nav ${isKeyboardVisible ? 'keyboard-visible' : ''}`}>
        <NavLink to="/app" end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <ClipboardList size={24} />
          <span>Mine Jobs</span>
        </NavLink>
        <NavLink to="/app/timer" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <CalendarDays size={24} />
          <span>Timer</span>
        </NavLink>
        <div className="nav-item-fab">
          <Can permission="job:create">
            <button className="fab-button" onClick={() => navigate('/app/job/new')} aria-label="Opret sag">
              <PlusCircle size={28} />
            </button>
          </Can>
        </div>
        <Can permission="user:manage">
          <NavLink to="/app/users" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Users size={24} />
            <span>Folk</span>
          </NavLink>
        </Can>
                <Can permission="user:manage">
          <NavLink to="/app/customers" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Building2 size={24} />
            <span>Kunder</span>
          </NavLink>
        </Can>
      </nav>
    </div>
    </DropdownProvider>
  );
};
