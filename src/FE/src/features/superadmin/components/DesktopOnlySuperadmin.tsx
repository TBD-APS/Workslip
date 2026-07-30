import type { ReactNode } from 'react';
import { LogOut, Monitor, ShieldCheck } from 'lucide-react';
import {
  DESKTOP_ONLY_SUPERADMIN_MESSAGE,
  isDesktopPlatform,
} from '../../../lib/platform';
import { useIsSuperAdmin } from '../../../providers/permissions';
import { useAuth } from '../../../providers/useAuth';
import { clearOrganizationSession } from '../organizationSession';
import '../routes/SuperAdmin.css';

interface DesktopOnlySuperadminScreenProps {
  onLogout: () => void;
}

export function DesktopOnlySuperadminScreen({
  onLogout,
}: DesktopOnlySuperadminScreenProps) {
  return (
    <main className="superadmin-desktop-only" aria-labelledby="superadmin-desktop-only-title">
      <section className="superadmin-desktop-only-card">
        <span className="superadmin-desktop-only-icon" aria-hidden="true">
          <Monitor size={34} />
          <ShieldCheck size={18} />
        </span>
        <h1 id="superadmin-desktop-only-title">
          {DESKTOP_ONLY_SUPERADMIN_MESSAGE.replace(/\.$/, '')}
        </h1>
        <p>
          Log ind på Workslip fra en computer for at administrere organisationer.
        </p>
        <button type="button" className="btn btn-secondary" onClick={onLogout}>
          <LogOut size={17} aria-hidden="true" />
          Log ud
        </button>
      </section>
    </main>
  );
}

export function DesktopOnlySuperadminBoundary({
  children,
}: {
  children: ReactNode;
}) {
  const isSuperadmin = useIsSuperAdmin();
  const { logout } = useAuth();

  if (!isSuperadmin || isDesktopPlatform()) {
    return <>{children}</>;
  }

  return (
    <DesktopOnlySuperadminScreen
      onLogout={() => {
        clearOrganizationSession();
        logout();
      }}
    />
  );
}
