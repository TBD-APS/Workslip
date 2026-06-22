import type { LucideIcon } from 'lucide-react';
import { ClipboardList, FileText, Wrench } from 'lucide-react';


export type DocumentTypeStatus = 'available' | 'coming_soon';

export interface DocumentType {
  id: string;
  label: string;
  description: string;
  icon: LucideIcon;
  path: string;
  permission?: 'job:create' | 'user:manage';
  status: DocumentTypeStatus;
}

export const DOCUMENT_TYPES: readonly DocumentType[] = [
  {
    id: 'job',
    label: 'Job',
    description: 'Opret en ny sag til en kunde.',
    icon: ClipboardList,
    path: '/app/job/new',
    permission: 'job:create',
    status: 'available',
  },
  {
    id: 'report',
    label: 'Rapport',
    description: 'Opret en vedligeholdelsesrapport',
    icon: FileText,
    path: '/app/document/report/new',
    status: 'coming_soon',
  },
  {
    id: 'service',
    label: 'Serviceordreseddel',
    description: 'Registrer udstyr brugt på sag.',
    icon: Wrench,
    path: '/app/document/service/new',
    status: 'coming_soon',
  },
];
