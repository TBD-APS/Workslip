import { Outlet, NavLink } from 'react-router-dom';
import { ClipboardList, PlusCircle, Settings, User } from 'lucide-react';

export default function AppLayout() {
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
          Workslip - sutwegwegweggweg
        </div>
        <div className="user-avatar">
          <User size={20} />
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
        <div className="nav-item-fab">
          <button className="fab-button">
            <PlusCircle size={28} />
          </button>
        </div>
        <NavLink to="/app/settings" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <Settings size={24} />
          <span>Indstillinger</span>
        </NavLink>
      </nav>
    </div>
  );
}
