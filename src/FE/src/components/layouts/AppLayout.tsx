import { useNavigate, useLocation, NavLink, Navigate, Outlet } from 'react-router-dom';
import { ClipboardList, Building2, CalendarDays, LogOut, PlusCircle, Settings, ShieldCheck, User, Users, Sun, Moon, Bell } from 'lucide-react';
import { useAuth } from '../../providers/useAuth';
import { Can, useCan, useIsSuperAdmin } from '../../providers/permissions';
import { useEffect, useState } from 'react';
import { DropdownProvider } from '../../providers/DropdownContext';
import { useTheme } from '../../providers/ThemeProvider';
import { CreateBottomSheet } from '../common/CreateBottomSheet';
import { NotificationsDrawer } from '../common/NotificationsDrawer';
import {
  AUDITOR_AUTHENTICATED_PATH,
  getAuthenticatedHomePath,
} from '../../features/auth/authenticatedDestination';
import {
  clearOrganizationSession,
  getOrganizationSession,
  restoreHomeOrganizationSession,
} from '../../features/superadmin/organizationSession';
import {
  DesktopOnlySuperadminScreen,
} from '../../features/superadmin/components/DesktopOnlySuperadmin';
import { isDesktopPlatform } from '../../lib/platform';
import '../../features/superadmin/organizationSession.css';
import '../../authenticated-base.css';
import '../../App.css';

export const AppLayout = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { logout, user } = useAuth();
  const isSuperadmin = useIsSuperAdmin();
  const canUseNotifications = useCan('notification:use');
  const isDesktop = isDesktopPlatform();
  const organizationSession = getOrganizationSession();
  const appHomePath = getAuthenticatedHomePath(user?.role);
  const isAuditorSession = appHomePath === AUDITOR_AUTHENTICATED_PATH;
  const [isKeyboardVisible, setIsKeyboardVisible] = useState(false);

  const { theme, toggle: toggleTheme } = useTheme();
  const [createSheetOpen, setCreateSheetOpen] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [unreadNotifications, setUnreadNotifications] = useState(0);

  const scrollToTopIfActive = (path: string) => {
    if (location.pathname === path) {
      document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
    }
  };

  const handleLogout = () => {
    clearOrganizationSession();
    logout();
    // Navigate immediately rather than waiting for ProtectedRoute to render
    // a <Navigate to="/login"> — avoids a single frame of protected content
    // still being visible after the user clicked logout, and prevents a
    // browser-back race where the protected URL is briefly visible again.
    navigate('/login', { replace: true });
  };

  const handleExitOrganizationSession = () => {
    if (!restoreHomeOrganizationSession()) {
      handleLogout();
      return;
    }

    // A full navigation recreates AuthProvider with the restored token and
    // clears all in-memory tenant queries before the Superadmin page renders.
    window.location.assign('/superadmin');
  };

  useEffect(() => {
    let focusCheckTimeoutId: number | undefined;

    const handleFocusChange = () => {
      const activeElement = document.activeElement;
      const isInput = activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement;
      setIsKeyboardVisible(isInput);
    };

    const handleFocusOut = () => {
      if (focusCheckTimeoutId !== undefined) {
        window.clearTimeout(focusCheckTimeoutId);
      }

      // Allow the next element to receive focus before deciding whether the
      // mobile keyboard-dependent layout should be restored.
      focusCheckTimeoutId = window.setTimeout(handleFocusChange, 50);
    };

    document.addEventListener('focusin', handleFocusChange);
    document.addEventListener('focusout', handleFocusOut);

    return () => {
      document.removeEventListener('focusin', handleFocusChange);
      document.removeEventListener('focusout', handleFocusOut);

      if (focusCheckTimeoutId !== undefined) {
        window.clearTimeout(focusCheckTimeoutId);
      }
    };
  }, []);

  if (isSuperadmin && !isDesktop) {
    return <DesktopOnlySuperadminScreen onLogout={handleLogout} />;
  }

  if (isSuperadmin && !organizationSession && location.pathname.startsWith('/app')) {
    return <Navigate to="/superadmin" replace />;
  }

  if (isAuditorSession && location.pathname === '/app') {
    return <Navigate to={AUDITOR_AUTHENTICATED_PATH} replace />;
  }

  const notificationLabel = unreadNotifications > 0
    ? `Notifikationer, ${unreadNotifications} ulæste`
    : 'Notifikationer';

  return (
    <DropdownProvider>
      <div className={`app-shell ${isKeyboardVisible ? 'keyboard-visible' : ''}`}>
        {/* Top Header for Mobile */}
      <header className="app-header">
        <button className="logo logo-header" onClick={() => navigate(isSuperadmin && !organizationSession ? '/superadmin' : appHomePath)}>
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
          {canUseNotifications && (
            <button
              type="button"
              onClick={() => setNotificationsOpen(true)}
              className="user-avatar notification-bell"
              aria-label={notificationLabel}
              title={notificationLabel}
            >
              <Bell size={18} />
              {unreadNotifications > 0 && (
                <span className="notification-badge" aria-hidden="true">
                  {unreadNotifications > 99 ? '99+' : unreadNotifications}
                </span>
              )}
            </button>
          )}
          {isDesktop && (
            <Can permission="organization:manage">
              <button
                type="button"
                onClick={() => navigate('/superadmin')}
                className="user-avatar"
                aria-label="Superadmin"
                title="Superadmin"
                aria-current={location.pathname === '/superadmin' ? 'page' : undefined}
              >
                <ShieldCheck size={18} />
              </button>
            </Can>
          )}
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
            onClick={toggleTheme}
            className="user-avatar"
            aria-label={theme === 'night' ? 'Skift til dagtilstand' : 'Skift til nattilstand'}
            title={theme === 'night' ? 'Dagtilstand' : 'Nattilstand'}
          >
            {theme === 'night' ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          {!isSuperadmin && (
            <button
              type="button"
              onClick={() => navigate('/app/profil')}
              className="user-avatar"
              aria-label="Profil"
              title="Profil"
            >
              {user?.displayName ? (
                <span className="user-avatar-initial">
                  {user.displayName.charAt(0).toUpperCase()}
                </span>
              ) : (
                <User size={18} />
              )}
            </button>
          )}
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

      {isSuperadmin && organizationSession && (
        <div className="organization-session-banner" role="status">
          <span>
            <Building2 size={17} aria-hidden="true" />
            Du arbejder i <strong>{organizationSession.name}</strong> som Superadmin.
          </span>
          <button type="button" className="btn btn-secondary" onClick={handleExitOrganizationSession}>
            Afslut organisationssession
          </button>
        </div>
      )}

      {/* Main Content Area */}
      <main className="app-content">
        <Outlet />
      </main>

      {/* Bottom Navigation (Mobile First) */}
      <nav className="bottom-nav">
        <NavLink to={appHomePath} end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive(appHomePath)}>
          <ClipboardList size={24} />
          <span>{isAuditorSession ? 'Rapporter' : 'Sager'}</span>
        </NavLink>
        <Can permission="worksheet:view">
          <NavLink to="/app/timer" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive('/app/timer')}>
            <CalendarDays size={24} />
            <span>Timer</span>
          </NavLink>
        </Can>
        <Can permission="user:manage">
          <NavLink to="/app/users" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive('/app/users')}>
            <Users size={24} />
            <span>Folk</span>
          </NavLink>
        </Can>
        <Can permission="customer:view">
          <NavLink to="/app/customers" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive('/app/customers')}>
            <Building2 size={24} />
            <span>Kunder</span>
          </NavLink>
        </Can>
      </nav>

      {/* Floating Create Button - only on Sager list */}
      {location.pathname === '/app' && (
        <Can permission="job:create">
          <button className="fab-create" onClick={() => setCreateSheetOpen(true)} aria-label="Opret ny sag">
            <PlusCircle size={22} />
          </button>
        </Can>
      )}
      <CreateBottomSheet isOpen={createSheetOpen} onClose={() => setCreateSheetOpen(false)} />
      {canUseNotifications && (
        <NotificationsDrawer
          isOpen={notificationsOpen}
          onClose={() => setNotificationsOpen(false)}
          onUnreadCountChange={setUnreadNotifications}
        />
      )}
    </div>
    </DropdownProvider>
  );
};
