import {
  BookOpen,
  Building2,
  CalendarDays,
  ClipboardList,
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
  showProfile: boolean;
}

export function buildQuickNavigatorCommands({
  homePath,
  homeLabel,
  canUseAppCommands,
  canViewTimer,
  canManageUsers,
  canViewCustomers,
  canViewDocs,
  canEditCustomers,
  canCreateJobs,
  canManageOrganization,
  showProfile,
}: QuickNavigatorCommandOptions): QuickNavigatorCommand[] {
  const commands: QuickNavigatorCommand[] = [];

  if (canUseAppCommands) {
    commands.push({
      id: 'home',
      label: homeLabel,
      description: homeLabel === 'Rapporter' ? 'Åbn rapportoversigten' : 'Åbn sagsoversigten',
      path: homePath,
      keywords: ['hjem', 'oversigt', 'sag', 'sager', 'rapport', 'rapporter'],
      icon: ClipboardList,
    });
  }

  if (canViewTimer) commands.push({
    id: 'timer',
    label: 'Timer',
    description: 'Åbn timer og timesedler',
    path: '/app/timer',
    keywords: ['timer', 'tid', 'timeseddel', 'arbejdstid'],
    icon: CalendarDays,
  });

  if (canManageUsers) commands.push({
    id: 'users',
    label: 'Folk',
    description: 'Åbn medarbejdere og brugere',
    path: '/app/users',
    keywords: ['folk', 'bruger', 'brugere', 'medarbejder', 'medarbejdere'],
    icon: Users,
  });

  if (canViewCustomers) commands.push({
    id: 'customers',
    label: 'Kunder',
    description: 'Åbn kundelisten',
    path: '/app/customers',
    keywords: ['kunde', 'kunder', 'firma', 'virksomhed'],
    icon: Building2,
  });

  if (canViewDocs) commands.push({
    id: 'docs',
    label: 'Docs',
    description: 'Åbn intern viden og dokumentation',
    path: '/app/docs',
    keywords: ['docs', 'dokument', 'dokumenter', 'viden', 'wiki', 'onboarding', 'procedure'],
    icon: BookOpen,
  });

  if (canCreateJobs) commands.push({
    id: 'new-job',
    label: 'Opret sag',
    description: 'Start oprettelse af en ny sag',
    path: '/app/create',
    keywords: ['ny sag', 'opret sag', 'opgave', 'create'],
    icon: PlusCircle,
  });

  if (canEditCustomers) commands.push({
    id: 'new-customer',
    label: 'Opret kunde',
    description: 'Opret en ny kunde',
    path: '/app/customers/new',
    keywords: ['ny kunde', 'opret kunde', 'firma', 'virksomhed'],
    icon: Building2,
  });

  if (canManageUsers) commands.push({
    id: 'settings',
    label: 'Indstillinger',
    description: 'Åbn administrative indstillinger',
    path: '/app/settings',
    keywords: ['indstillinger', 'settings', 'administration', 'admin'],
    icon: Settings,
  });

  if (showProfile) commands.push({
    id: 'profile',
    label: 'Profil',
    description: 'Åbn din profil',
    path: '/app/profil',
    keywords: ['profil', 'mig', 'konto'],
    icon: UserCircle,
  });

  if (canManageOrganization) commands.push({
    id: 'superadmin',
    label: 'Superadmin',
    description: 'Åbn organisationsadministration',
    path: '/superadmin',
    keywords: ['superadmin', 'organisation', 'organization'],
    icon: ShieldCheck,
  });

  return commands;
}
