import { useNavigate, NavLink, Outlet } from 'react-router-dom';
import { ClipboardList, FileCheck2, Building2, LogOut, PlusCircle, Settings, User, Users } from 'lucide-react';
import { useAuth } from '../../providers/useAuth';
import { Can } from '../../providers/permissions';

export const AppLayout = () => {
  const navigate = useNavigate();
  const { logout, user } = useAuth();

  const handleLogout = () => {
    logout();
  };

  return (
    <div className="app-shell">
      {/* Top Header for Mobile */}
      <header className="app-header">
        <div className="logo" style={{ fontSize: '1.25rem' }}>
          <svg className="logo-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
          Workslip
        </div>
        <div className="app-header-actions">
          <span className="app-header-user" title={user?.email ?? ''}>
            <User size={16} />
            <span>{user?.displayName ?? user?.email ?? ''}</span>
          </span>
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
      <nav className="bottom-nav">
        <NavLink to="/app" end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <ClipboardList size={24} />
          <span>Mine Jobs</span>
        </NavLink>
        <Can permission="user:manage">
          <NavLink to="/app/users" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Users size={24} />
            <span>Folk</span>
          </NavLink>
        </Can>
        <Can permission="job:viewAll">
          <NavLink to="/app/customers" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Building2 size={24} />
            <span>Kunder</span>
          </NavLink>
        </Can>
        <div className="nav-item-fab">
          <Can permission="job:create">
            <button className="fab-button" onClick={() => navigate('/app/job/new')} aria-label="Opret sag">
              <PlusCircle size={28} />
            </button>
          </Can>
        </div>
        <Can permission="job:viewAll">
          <NavLink to="/app/completed" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <FileCheck2 size={24} />
            <span>Afsluttede sager</span>
          </NavLink>
        </Can>
        <NavLink to="/app/settings" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <Settings size={24} />
          <span>Indstillinger</span>
        </NavLink>
      </nav>
    </div>
  );
};
