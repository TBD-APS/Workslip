import { useNavigate, useLocation, NavLink, Navigate, Outlet } from 'react-router-dom';
import { ClipboardList, Building2, CalendarDays, LogOut, PlusCircle, Settings, ShieldCheck, User, Users, Sun, Moon, Bell, Search } from 'lucide-react';
import { useAuth } from '../../providers/useAuth';
import { Can, useCan, useIsSuperAdmin } from '../../providers/permissions';
import { useRef, useState } from 'react';
import { DropdownProvider } from '../../providers/DropdownContext';
import { useTheme } from '../../providers/ThemeProvider';
import { CreateBottomSheet } from '../common/CreateBottomSheet';
import { NotificationsDrawer } from '../common/NotificationsDrawer';
import { QuickNavigator } from '../common/QuickNavigator';
import { ProfileAvatar } from '../../features/images/ProfileAvatar';
import {
  AUDITOR_AUTHENTICATED_PATH,
  getAuthenticatedHomePath,
} from '../../features/auth/authenticatedDestination';
import {
  clearOrganizationSession,
  getOrganizationSession,
  restoreHomeOrganizationSession,
} from '../../features/superadmin/organizationSession';
import '../../features/superadmin/organizationSession.css';
import '../../authenticated-base.css';
import '../../App.css';
import '../../features/jobs/jobWizardTheme.css';
import './AppLayout.focus.css';
import '../../farvelab-theme.css';
import {
  AppScrollRestoreBoundary,
  useAppRouteScrollManager,
} from '../../hooks/useAppRouteScroll';

export const AppLayout = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { logout, user } = useAuth();
  const isSuperadmin = useIsSuperAdmin();
  const canUseNotifications = useCan('notification:use');
  const canViewTimer = useCan('worksheet:view');
  const canManageUsers = useCan('user:manage');
  const canViewCustomers = useCan('customer:view');
  const canEditCustomers = useCan('customer:edit');
  const canCreateJobs = useCan('job:create');
  const canViewAllJobs = useCan('job:viewAll');
  const canManageOrganization = useCan('organization:manage');
  const organizationSession = getOrganizationSession();
  const appHomePath = getAuthenticatedHomePath(user?.role);
  const isAuditorSession = appHomePath === AUDITOR_AUTHENTICATED_PATH;
  const canUseAppCommands = !isSuperadmin || Boolean(organizationSession);
  const canSearchJobs = canUseAppCommands && !isAuditorSession;

  const { theme, toggle: toggleTheme } = useTheme();
  const [createSheetOpen, setCreateSheetOpen] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [quickNavigatorOpen, setQuickNavigatorOpen] = useState(false);
  const [unreadNotifications, setUnreadNotifications] = useState(0);
  const scrollContainerRef = useRef<HTMLDivElement>(null);

  const restoreScrollKey = useAppRouteScrollManager(scrollContainerRef);

  const scrollToTopIfActive = (path: string) => {
    if (location.pathname === path) {
      scrollContainerRef.current?.scrollTo({ top: 0, behavior: 'smooth' });
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
    <AppScrollRestoreBoundary restoreKey={restoreScrollKey}>
    <DropdownProvider>
      <div ref={scrollContainerRef} className="app-shell">
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
          <button
            type="button"
            onClick={() => setQuickNavigatorOpen(true)}
            className="user-avatar quick-nav-header-trigger"
            aria-label="Hurtig navigation"
            aria-haspopup="dialog"
            aria-expanded={quickNavigatorOpen}
            title="Hurtig navigation (Ctrl+K)"
          >
            <Search size={18} />
          </button>
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
              <ProfileAvatar userId={user?.id} displayName={user?.displayName} />
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

      <QuickNavigator
        isOpen={quickNavigatorOpen}
        onOpen={() => setQuickNavigatorOpen(true)}
        onClose={() => setQuickNavigatorOpen(false)}
        homePath={appHomePath}
        homeLabel={isAuditorSession ? 'Rapporter' : 'Sager'}
        canUseAppCommands={canUseAppCommands}
        canSearchJobs={canSearchJobs}
        canViewAllJobs={canViewAllJobs}
        currentUserId={user?.id}
        canViewTimer={canUseAppCommands && canViewTimer}
        canManageUsers={canUseAppCommands && canManageUsers}
        canViewCustomers={canUseAppCommands && canViewCustomers}
        canEditCustomers={canUseAppCommands && canEditCustomers}
        canCreateJobs={canUseAppCommands && canCreateJobs}
        canManageOrganization={canManageOrganization}
        showProfile={canUseAppCommands && !isSuperadmin}
      />

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
        <button
          type="button"
          className={`nav-item quick-nav-mobile-trigger ${quickNavigatorOpen ? 'active' : ''}`}
          onClick={() => setQuickNavigatorOpen(true)}
          aria-label="Hurtig navigation"
          aria-haspopup="dialog"
          aria-expanded={quickNavigatorOpen}
        >
          <Search size={24} />
          <span>Søg</span>
        </button>
      </nav>

      {/* Floating Create Button - only on Sager list */}
      {location.pathname === '/app' && (
        <Can permission="job:create">
          <button className="fab-create" onClick={() => setCreateSheetOpen(true)} aria-label="Opret ny sag">
            <PlusCircle size={22} />
          </button>
        </Can>
      )}

      {/* Floating Create Button - on Customers list */}
      {location.pathname === '/app/customers' && (
        <Can permission="user:manage">
          <button className="fab-create" onClick={() => setCreateSheetOpen(true)} aria-label="Opret ny kunde">
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
    </AppScrollRestoreBoundary>
  );
};
