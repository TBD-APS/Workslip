import { hasRole, ROLES } from '../providers/permissions';

let appLayoutModulePromise: Promise<typeof import('../components/layouts/AppLayout')> | null = null;
let overviewModulePromise: Promise<typeof import('../features/overview/routes/Overview')> | null = null;
let auditorModulePromise: Promise<typeof import('../features/auditor/routes/AuditorReportList')> | null = null;
let superAdminModulePromise: Promise<typeof import('../features/superadmin/routes/SuperAdmin')> | null = null;

function loadAppLayoutModule() {
  if (!appLayoutModulePromise) {
    appLayoutModulePromise = import('../components/layouts/AppLayout').catch((error) => {
      appLayoutModulePromise = null;
      throw error;
    });
  }

  return appLayoutModulePromise;
}

function loadOverviewModule() {
  if (!overviewModulePromise) {
    overviewModulePromise = import('../features/overview/routes/Overview').catch((error) => {
      overviewModulePromise = null;
      throw error;
    });
  }

  return overviewModulePromise;
}

function loadAuditorModule() {
  if (!auditorModulePromise) {
    auditorModulePromise = import('../features/auditor/routes/AuditorReportList').catch((error) => {
      auditorModulePromise = null;
      throw error;
    });
  }

  return auditorModulePromise;
}

function loadSuperAdminModule() {
  if (!superAdminModulePromise) {
    superAdminModulePromise = import('../features/superadmin/routes/SuperAdmin').catch((error) => {
      superAdminModulePromise = null;
      throw error;
    });
  }

  return superAdminModulePromise;
}

/**
 * Warm the authenticated shell as soon as a stored token exists. When the
 * freshly authenticated role is known, warm the route the user will actually
 * land on so the post-login transition does not fall through to route-level
 * Suspense for a stale default destination.
 */
export async function preloadPrimaryAppRoute(role?: string | null): Promise<void> {
  const loads: Promise<unknown>[] = [loadAppLayoutModule()];

  if (hasRole(role, ROLES.Superadmin)) {
    loads.push(loadSuperAdminModule());
  } else if (hasRole(role, ROLES.Auditor)) {
    loads.push(loadAuditorModule());
  } else if (role) {
    loads.push(loadOverviewModule());
  }

  await Promise.all(loads);
}
