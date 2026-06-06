import { Building2, CheckCircle2, ClipboardList, FileSpreadsheet, FileText, ShieldCheck } from 'lucide-react';

export const JOB_STEPS = [
  { icon: Building2, label: 'Sagsdetaljer' },
  { icon: FileText, label: 'Anlægstyper' },
  { icon: ClipboardList, label: 'Kontrolpunkter' },
  { icon: FileSpreadsheet, label: 'Arbejdssedler' },
  { icon: ShieldCheck, label: 'Attestering' },
  { icon: CheckCircle2, label: 'Afslutning' },
] as const;
