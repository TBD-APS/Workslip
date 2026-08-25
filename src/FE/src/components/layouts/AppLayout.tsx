import { useNavigate, useLocation, NavLink, Navigate, Outlet } from 'react-router-dom';
import { BookOpen, ClipboardList, Building2, CalendarDays, LogOut, Menu, PlusCircle, Settings, ShieldCheck, User, Users, Sun, Moon, Bell, Search, X } from 'lucide-react';
import { useAuth } from '../../providers/useAuth';
import { Can, useCan, useIsSuperAdmin } from '../../providers/permissions';
import { useEffect, useRef, useState } from 'react';
import { DropdownProvider } from '../../providers/DropdownContext';
import { useTheme } from '../../providers/ThemeProvider';
import { CreateBottomSheet } from '../common/CreateBottomSheet';
import { NotificationsDrawer } from '../common/NotificationsDrawer';
import { QuickNavigator } from '../common/QuickNavigator';
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
import './AppLayout.desktop.css';
import '../../farvelab-theme.css';
import {
  AppScrollRestoreBoundary,
  useAppRouteScrollManager,
} from '../../hooks/useAppRouteScroll';

const DESKTOP_RAIL_COLLAPSED_KEY = 'workslip.desktopRailCollapsed';

const readDesktopRailCollapsed = () => {
  if (typeof window === 'undefined') return false;
  try {
    return window.localStorage.getItem(DESKTOP_RAIL_COLLAPSED_KEY) === 'true';
  } catch {
    return false;
  }
};

export const AppLayout = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { logout, user } = useAuth();
  const isSuperadmin = useIsSuperAdmin();
  const canUseNotifications = useCan('notification:use');
  const canViewTimer = useCan('worksheet:view');
  const canManageUsers = useCan('user:manage');
  const canViewCustomers = useCan('customer:view');
  const canViewDocs = useCan('docs:view');
  const canEditCustomers = useCan('customer:edit');
  const canCreateJobs = useCan('job:create');
  const canViewAllJobs = useCan('job:viewAll');
  const canManageOrganization = useCan('organization:manage');
  const organizationSession = getOrganizationSession();
  const appHomePath = getAuthenticatedHomePath(user?.role);
  const isAuditorSession = appHomePath === AUDITOR_AUTHENTICATED_PATH;
  const canUseAppCommands = !isSuperadmin || Boolean(organizationSession);
  const canSearchJobs = canUseAppCommands && !isAuditorSession;
  const profileInitial = user?.displayName?.trim().charAt(0).toUpperCase()
    || user?.email?.trim().charAt(0).toUpperCase()
    || '?';

  const { theme, toggle: toggleTheme } = useTheme();
  const [createSheetOpen, setCreateSheetOpen] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [quickNavigatorOpen, setQuickNavigatorOpen] = useState(false);
  const [settingsMenuOpen, setSettingsMenuOpen] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [mobileNavRoute, setMobileNavRoute] = useState(location.pathname);
  const [unreadNotifications, setUnreadNotifications] = useState(0);
  const [desktopRailCollapsed, setDesktopRailCollapsed] = useState(readDesktopRailCollapsed);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const settingsMenuRef = useRef<HTMLDivElement>(null);

  const restoreScrollKey = useAppRouteScrollManager(scrollContainerRef);

  useEffect(() => {
    if (!settingsMenuOpen) return undefined;

    const handlePointerDown = (event: PointerEvent) => {
      if (!settingsMenuRef.current?.contains(event.target as Node)) {
        setSettingsMenuOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setSettingsMenuOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [settingsMenuOpen]);

  // Close the mobile navigation drawer whenever the route changes. Adjusting
  // state during render (rather than in an effect) avoids a cascading re-render.
  if (location.pathname !== mobileNavRoute) {
    setMobileNavRoute(location.pathname);
    setMobileNavOpen(false);
  }

  // Let Escape close the mobile drawer.
  useEffect(() => {
    if (!mobileNavOpen) return undefined;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMobileNavOpen(false);
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [mobileNavOpen]);

  const scrollToTopIfActive = (path: string) => {
    if (location.pathname === path) {
      scrollContainerRef.current?.scrollTo({ top: 0, behavior: 'smooth' });
    }
  };

  const handleDesktopRailToggle = (collapsed: boolean) => {
    setDesktopRailCollapsed(collapsed);
    try {
      window.localStorage.setItem(DESKTOP_RAIL_COLLAPSED_KEY, String(collapsed));
    } catch {
      // Keep the in-memory preference when storage is unavailable.
    }
  };

  const handleLogout = () => {
    clearOrganizationSession();
    logout();
    navigate('/login', { replace: true });
  };

  const handleExitOrganizationSession = () => {
    if (!restoreHomeOrganizationSession()) {
      handleLogout();
      return;
    }

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
      <div id="app-shell" ref={scrollContainerRef} className={`app-shell${mobileNavOpen ? ' mobile-nav-open' : ''}`}>
      <header className="app-header">
        <button
          id="mobile-nav-toggle"
          type="button"
          className="app-nav-toggle"
          onClick={() => setMobileNavOpen((open) => !open)}
          aria-label={mobileNavOpen ? 'Luk menu' : 'Åbn menu'}
          aria-controls="bottom-nav"
          aria-expanded={mobileNavOpen}
        >
          {mobileNavOpen ? <X size={22} aria-hidden="true" /> : <Menu size={22} aria-hidden="true" />}
        </button>
        <button className="logo logo-header" onClick={() => navigate(isSuperadmin && !organizationSession ? '/superadmin' : appHomePath)}>
          <svg className="logo-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 17L12 22L22 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            <path d="M2 12L12 17L22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
          Workslip
        </button>
        <div className="app-header-actions">
          {canUseNotifications && (
            <button
              id="app-notifications-button"
              type="button"
              onClick={() => setNotificationsOpen(true)}
              className="user-avatar notification-bell"
              aria-label={notificationLabel}
              title={notificationLabel}
            >
              <Bell size={18} />
              {unreadNotifications > 0 && (
                <span
                  id="app-notifications-badge"
                  className="notification-badge"
                  data-count={unreadNotifications}
                  aria-hidden="true"
                >
                  {unreadNotifications > 99 ? '99+' : unreadNotifications}
                </span>
              )}
            </button>
          )}
          <div ref={settingsMenuRef} className="app-header-settings">
            <button
              id="account-menu-button"
              type="button"
              data-testid="account-menu-button"
              onClick={() => setSettingsMenuOpen((open) => !open)}
              className="user-avatar"
              aria-label="Profil og konto"
              aria-haspopup="menu"
              aria-expanded={settingsMenuOpen}
              title="Profil"
              aria-current={location.pathname.startsWith('/app/profil') || location.pathname.startsWith('/app/settings') || location.pathname.startsWith('/app/docs') || location.pathname === '/superadmin' ? 'page' : undefined}
            >
              <span aria-hidden="true">{profileInitial}</span>
            </button>
            {settingsMenuOpen && (
              <div id="account-menu" className="app-header-settings-menu" role="menu" aria-label="Profil og konto" data-testid="account-menu">
                {!isSuperadmin && (
                  <button
                    id="account-menu-profile"
                    type="button"
                    className="app-header-settings-item"
                    role="menuitem"
                    onClick={() => {
                      setSettingsMenuOpen(false);
                      navigate('/app/profil');
                    }}
                  >
                    <User size={16} aria-hidden="true" />
                    <span>Profil</span>
                  </button>
                )}
                {canManageOrganization && (
                  <button
                    type="button"
                    className="app-header-settings-item"
                    role="menuitem"
                    onClick={() => {
                      setSettingsMenuOpen(false);
                      navigate('/superadmin');
                    }}
                  >
                    <ShieldCheck size={16} aria-hidden="true" />
                    <span>Superadmin</span>
                  </button>
                )}
                {canViewDocs && canUseAppCommands && (
                  <button
                    id="account-menu-docs"
                    type="button"
                    className="app-header-settings-item"
                    role="menuitem"
                    onClick={() => {
                      setSettingsMenuOpen(false);
                      navigate('/app/docs');
                    }}
                  >
                    <BookOpen size={16} aria-hidden="true" />
                    <span>Docs</span>
                  </button>
                )}
                <button
                  id="account-menu-theme"
                  type="button"
                  className="app-header-settings-item"
                  role="menuitem"
                  onClick={toggleTheme}
                >
                  {theme === 'night'
                    ? <Sun size={16} aria-hidden="true" />
                    : <Moon size={16} aria-hidden="true" />}
                  <span>{theme === 'night' ? 'Dagtilstand' : 'Nattilstand'}</span>
                </button>
                {canManageUsers && (
                  <button
                    id="account-menu-settings"
                    type="button"
                    className="app-header-settings-item"
                    role="menuitem"
                    onClick={() => {
                      setSettingsMenuOpen(false);
                      navigate('/app/settings');
                    }}
                  >
                    <Settings size={16} aria-hidden="true" />
                    <span>Indstillinger</span>
                  </button>
                )}
                <button
                  id="logout-button"
                  type="button"
                  data-testid="logout-button"
                  className="app-header-settings-item app-header-settings-item--danger"
                  role="menuitem"
                  onClick={() => {
                    setSettingsMenuOpen(false);
                    handleLogout();
                  }}
                >
                  <LogOut size={16} aria-hidden="true" />
                  <span>Log ud</span>
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <QuickNavigator
        isOpen={quickNavigatorOpen}
        onOpen={() => setQuickNavigatorOpen(true)}
        onClose={() => setQuickNavigatorOpen(false)}
        homePath={appHomePath}
        homeLabel={isAuditorSession ? 'Rapporter' : 'Overblik'}
        canUseAppCommands={canUseAppCommands}
        canSearchJobs={canSearchJobs}
        canViewAllJobs={canViewAllJobs}
        currentUserId={user?.id}
        canViewTimer={canUseAppCommands && canViewTimer}
        canManageUsers={canUseAppCommands && canManageUsers}
        canViewCustomers={canUseAppCommands && canViewCustomers}
        canViewDocs={canUseAppCommands && canViewDocs}
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

      <main className="app-content">
        <Outlet />
      </main>

      <button
        type="button"
        className="mobile-nav-scrim"
        aria-hidden="true"
        tabIndex={-1}
        onClick={() => setMobileNavOpen(false)}
      />

      <nav id="bottom-nav" className="bottom-nav" onClick={() => setMobileNavOpen(false)}>
        <NavLink id="bottom-nav-home" to={appHomePath} end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive(appHomePath)}>
          <ClipboardList size={24} />
          <span>{isAuditorSession ? 'Rapporter' : 'Overblik'}</span>
        </NavLink>
        <Can permission="worksheet:view">
          <NavLink id="bottom-nav-timer" to="/app/timer" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive('/app/timer')}>
            <CalendarDays size={24} />
            <span>Timer</span>
          </NavLink>
        </Can>
        <Can permission="user:manage">
          <NavLink id="bottom-nav-people" to="/app/users" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive('/app/users')}>
            <Users size={24} />
            <span>Folk</span>
          </NavLink>
        </Can>
        <Can permission="customer:view">
          <NavLink id="bottom-nav-customers" to="/app/customers" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`} onClick={() => scrollToTopIfActive('/app/customers')}>
            <Building2 size={24} />
            <span>Kunder</span>
          </NavLink>
        </Can>
        <button
          id="bottom-nav-search"
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
        <div className="desktop-rail-toggle-wrap">
          <input
            id="desktop-rail-toggle"
            className="desktop-rail-toggle-input"
            type="checkbox"
            checked={desktopRailCollapsed}
            onChange={(event) => handleDesktopRailToggle(event.currentTarget.checked)}
            aria-label="Skjul eller vis navigation"
          />
          <label htmlFor="desktop-rail-toggle" className="desktop-rail-toggle">
            <span className="desktop-rail-toggle-icon" aria-hidden="true" />
            <span className="desktop-rail-toggle-label desktop-rail-toggle-label--collapse">Skjul</span>
            <span className="desktop-rail-toggle-label desktop-rail-toggle-label--expand">Vis</span>
          </label>
        </div>
      </nav>

      {location.pathname === '/app' && (
        <Can permission="job:create">
          <button className="fab-create" onClick={() => setCreateSheetOpen(true)} aria-label="Opret ny sag">
            <PlusCircle size={22} />
          </button>
        </Can>
      )}

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
