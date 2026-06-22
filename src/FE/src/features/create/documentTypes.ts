import type { LucideIcon } from 'lucide-react';
import { ClipboardList, FileText, Wrench } from 'lucide-react';

/**
 * Document-type catalogue for the create picker.
 *
 * Adding a new type:
 *  1. Add an entry below with `status: 'available'` and a target `path`.
 *  2. Build the matching route + page.
 *  3. Gate the route / FAB visibility with the same permission if needed.
 *
 * Placeholders use `status: 'coming_soon'` — they're rendered as disabled
 * tiles so the user can see what's planned without crashing into a dead end.
 *
 * `permission` is the minimum permission required to *open* the type's
 * creation page. The picker page itself is still gated by `job:create` today;
 * once we have a more general `document:create` permission the gate should
 * move here.
 */
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
    description: 'Skriv en rapport direkte uden en tilknyttet sag.',
    icon: FileText,
    path: '/app/document/report/new',
    status: 'coming_soon',
  },
  {
    id: 'service',
    label: 'Serviceordreseddel',
    description: 'Registrer en serviceopgave på eksisterende udstyr.',
    icon: Wrench,
    path: '/app/document/service/new',
    status: 'coming_soon',
  },
];
