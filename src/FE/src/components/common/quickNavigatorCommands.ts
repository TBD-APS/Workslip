import {
  BarChart3,
  Building2,
  CalendarDays,
  ClipboardList,
  FileSpreadsheet,
  PackageSearch,
  PlusCircle,
  Settings,
  ShieldCheck,
  UserCircle,
  Users,
  type LucideIcon,
} from 'lucide-react';

export type QuickNavigatorCommand = {
  id: string;
  label: string;
  description: string;
  path: string;
  keywords: string[];
  icon: LucideIcon;
};

interface QuickNavigatorCommandOptions {
  homePath: string;
  homeLabel: string;
  canUseAppCommands: boolean;
  canViewTimer: boolean;
  canManageUsers: boolean;
  canViewCustomers: boolean;
  canViewDocs: boolean;
  canEditCustomers: boolean;
  canCreateJobs: boolean;
  canManageOrganization: boolean;
  canViewLeaderAnalysis?: boolean;
  showProfile: boolean;
}

export function buildQuickNavigatorCommands({
  homePath,
  homeLabel,
  canUseAppCommands,
  canViewTimer,
  canManageUsers,
  canViewCustomers,
  canEditCustomers,
  canCreateJobs,
  canManageOrganization,
  canViewLeaderAnalysis,
  showProfile,
}: QuickNavigatorCommandOptions): QuickNavigatorCommand[] {
  const commands: QuickNavigatorCommand[] = [];

  if (canUseAppCommands) {
    commands.push({
      id: 'home',
      label: homeLabel,
      description: homeLabel === 'Rapporter' ? 'Åbn rapportoversigten' : 'Åbn overblikket',
      path: homePath,
      keywords: ['hjem', 'overblik', 'oversigt', 'sag', 'sager', 'rapport', 'rapporter'],
      icon: ClipboardList,
    });
  }

  // Inventory uses the same user-only capability boundary as job creation.
  // Auditors do not have this permission and must not be offered a route whose API is user-policy protected.
  if (canUseAppCommands && canCreateJobs) {
    commands.push({
      id: 'inventory',
      label: 'Lager',
      description: 'Scan QR-koder og registrer varer',
      path: '/app/lager',
      keywords: ['lager', 'vare', 'varer', 'qr', 'scan', 'scanner', 'materialer'],
      icon: PackageSearch,
    });
  }

  if (canViewTimer) commands.push({
    id: 'timer', label: 'Timer', description: 'Åbn timer', path: '/app/timer',
    keywords: ['timer', 'tid', 'timeseddel', 'arbejdstid'], icon: CalendarDays,
  });
  if (canManageUsers) commands.push({
    id: 'users', label: 'Folk', description: 'Åbn medarbejdere', path: '/app/users',
    keywords: ['folk', 'bruger', 'brugere', 'medarbejder', 'medarbejdere'], icon: Users,
  });
  if (canViewCustomers) commands.push({
    id: 'customers', label: 'Kunder', description: 'Åbn kunder', path: '/app/customers',
    keywords: ['kunde', 'kunder', 'firma', 'virksomhed'], icon: Building2,
  });
  if (canCreateJobs) commands.push({
    id: 'new-job', label: 'Opret sag', description: 'Opret en ny sag', path: '/app/create',
    keywords: ['ny sag', 'opret sag', 'opgave'], icon: PlusCircle,
  });
  if (canEditCustomers) commands.push({
    id: 'new-customer', label: 'Opret kunde', description: 'Opret en ny kunde', path: '/app/customers/new',
    keywords: ['ny kunde', 'opret kunde', 'firma', 'virksomhed'], icon: Building2,
  });
  if (canManageUsers) {
    commands.push({
      id: 'inventory-onboarding',
      label: 'Lageropsætning',
      description: 'Importér vareliste og print QR-labels',
      path: '/app/lager/opsaetning',
      keywords: ['lager', 'csv', 'excel', 'import', 'qr', 'labels', 'print', 'varer'],
      icon: FileSpreadsheet,
    });
    commands.push({
      id: 'settings', label: 'Indstillinger', description: 'Åbn indstillinger', path: '/app/settings',
      keywords: ['indstillinger', 'settings', 'administration', 'admin'], icon: Settings,
    });
  }
  if (showProfile) commands.push({
    id: 'profile', label: 'Profil', description: 'Åbn din profil', path: '/app/profil',
    keywords: ['profil', 'mig', 'konto'], icon: UserCircle,
  });
  if (canViewLeaderAnalysis) commands.push({
    id: 'leader-analysis', label: 'Lederanalyse', description: 'Åbn lederanalyse og driftsnøgletal', path: '/app/lederanalyse',
    keywords: ['leder', 'lederanalyse', 'analyse', 'ledelse', 'kpi', 'drift', 'bemanning'], icon: BarChart3,
  });
  if (canManageOrganization) commands.push({
    id: 'superadmin', label: 'Superadmin', description: 'Åbn organisationsadministration', path: '/superadmin',
    keywords: ['superadmin', 'organisation', 'organization'], icon: ShieldCheck,
  });
  return commands;
}