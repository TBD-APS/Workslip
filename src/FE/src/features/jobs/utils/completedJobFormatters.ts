import type { InstallationTypeResponse, JobReportSummaryViewModel } from '../../../api/generated/models';
import { hasText } from '../../../lib/formatUtils';

export function formatReportNumber(job: Pick<JobReportSummaryViewModel, 'id' | 'reportNumber'>) {
  return `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`;
}

export function formatWorkKind(job: JobReportSummaryViewModel) {
  const workKind = job.work.workKind;
  if (!workKind) return null;
  if (workKind.customWorkKind) return `${workKind.label}: ${workKind.customWorkKind}`;
  return workKind.label;
}

export function formatInstallationTypeNames(installationTypes: InstallationTypeResponse[]) {
  const names = installationTypes.map((installationType) => installationType.name).filter(hasText);
  return names.length > 0 ? names.join(', ') : null;
}

export function formatClosureFlags(job: JobReportSummaryViewModel) {
  const labels = job.work.closureFlags.map((flag) => flag.label).filter(hasText);
  return labels.length > 0 ? labels.join(', ') : null;
}
