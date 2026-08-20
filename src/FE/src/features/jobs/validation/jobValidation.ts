import type { ReferenceDataResponse } from '../../../api/generated/models';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import { ClosureFlagLabels } from '../closureFlagLabels';
import type { JobForm } from '../types';

export type JobValidationIssue = {
  code: string;
  field: string;
  message: string;
  step: number;
  targetId: string;
  actionLabel: string;
};

export type JobValidationContext = {
  form: JobForm;
  referenceData: ReferenceDataResponse | null;
  worksheetCount: number;
  reportNumberReadOnly?: boolean;
};

export type BackendFieldError = {
  field: string;
  message: string;
};

const issue = (
  code: string,
  field: string,
  message: string,
  step: number,
  targetId: string,
  actionLabel: string,
): JobValidationIssue => ({ code, field, message, step, targetId, actionLabel });

export function getJobStepValidationIssues(
  context: JobValidationContext,
  step: number,
): JobValidationIssue[] {
  const { form, referenceData, worksheetCount, reportNumberReadOnly = false } = context;

  if (step === 0) {
    const issues: JobValidationIssue[] = [];

    if (!reportNumberReadOnly && form.reportNumber.trim().length === 0) {
      issues.push(issue(
        'job.reportNumber.required',
        'reportNumber',
        'Sagsnummer mangler.',
        0,
        'job-report-number',
        'Udfyld sagsnummer',
      ));
    }

    if (form.jobType === 'Diverse') return issues;

    const name = form.customerSnapshot?.name ?? '';
    const email = form.customerSnapshot?.email ?? '';
    const phone = form.customerSnapshot?.phone ?? '';

    if (name.trim().length === 0) {
      issues.push(issue(
        'customer.name.required',
        'customerSnapshot.name',
        'Kundenavn mangler.',
        0,
        'job-customer-name',
        'Udfyld kundenavn',
      ));
    }

    const emailError = validateEmail(email);
    if (emailError) {
      issues.push(issue(
        'customer.email.invalid',
        'customerSnapshot.email',
        emailError,
        0,
        'job-customer-email',
        'Ret e-mail',
      ));
    }

    const phoneError = validatePhoneNumber(phone);
    if (phoneError) {
      issues.push(issue(
        'customer.phone.invalid',
        'customerSnapshot.phone',
        phoneError,
        0,
        'job-customer-phone',
        'Ret telefonnummer',
      ));
    }

    return issues;
  }

  if (step === 1) {
    if (form.jobType === 'Diverse') return [];

    if (form.work.categoryIds.length === 0) {
      return [issue(
        'work.installationType.required',
        'work.categoryIds',
        'Vælg mindst én anlægstype.',
        1,
        'job-installation-types',
        'Vælg anlægstype',
      )];
    }

    if (form.work.workKind.length === 0) {
      return [issue(
        'work.kind.required',
        'work.workKind',
        'Vælg en opgavetype.',
        1,
        'job-work-kind',
        'Vælg opgavetype',
      )];
    }

    const selectedWorkKind = referenceData?.workKinds.find(
      (kind) => kind.normalizedLabel === form.work.workKind,
    );
    if (selectedWorkKind?.requiresCustomWorkKind && form.work.customWorkKind.trim().length === 0) {
      return [issue(
        'work.customKind.required',
        'work.customWorkKind',
        'Beskriv den valgte anden opgavetype.',
        1,
        'job-custom-work-kind',
        'Beskriv opgavetype',
      )];
    }

    return [];
  }

  if (step === 2) {
    if (form.jobType === 'Diverse') return [];

    const selectedInstallationTypes = referenceData?.installationTypes.filter(
      (installationType) => form.work.categoryIds.includes(installationType.id),
    ) ?? [];

    for (const installationType of selectedInstallationTypes) {
      for (const category of installationType.categories) {
        const compositeId = `${installationType.id}-${category.id}`;
        const isIrrelevant = form.work.irrelevantCategoryIds.includes(compositeId);
        if (isIrrelevant) continue;

        const hasSelectedControlPoint = category.controlPoints.some(
          (controlPoint) => form.work.controlPointSelections[controlPoint.id],
        );
        if (!hasSelectedControlPoint) {
          return [issue(
            'work.controlPoint.required',
            `work.controlPoints.${compositeId}`,
            `Vælg mindst ét kontrolpunkt for ${installationType.name} · ${capitalize(category.name)}, eller markér kategorien som irrelevant.`,
            2,
            `job-control-category-${compositeId}`,
            'Ret kontrolpunkt',
          )];
        }
      }
    }

    return [];
  }

  if (step === 3) {
    return worksheetCount > 0
      ? []
      : [issue(
          'worksheets.required',
          'worksheets',
          'Der mangler en timeseddel.',
          3,
          'job-worksheet-add-trigger',
          'Tilføj timeseddel',
        )];
  }

  if (step === 4) {
    const flags = form.work.closureFlags ?? [];
    if (flags.length === 0) {
      return [issue(
        'work.closureFlags.required',
        'work.closureFlags',
        'Vælg mindst én afslutningsstatus.',
        4,
        'job-closure-flags',
        'Vælg afslutningsstatus',
      )];
    }

    if (flags.length === 1 && flags[0] === ClosureFlagLabels.OperationMaintenanceInstructions) {
      return [issue(
        'work.closureFlags.primaryRequired',
        'work.closureFlags',
        'Vælg også Ikke færdig, Færdig eller Klar til faktura.',
        4,
        'job-closure-flags',
        'Vælg sagens status',
      )];
    }

    return [];
  }

  return [];
}

export function getJobValidationIssues(
  context: JobValidationContext,
  throughStep = 4,
): JobValidationIssue[] {
  const issues: JobValidationIssue[] = [];
  for (let step = 0; step <= throughStep; step += 1) {
    issues.push(...getJobStepValidationIssues(context, step));
  }
  return issues;
}

export function mapBackendValidationIssues(errors: BackendFieldError[]): JobValidationIssue[] {
  return errors.map((error, index) => {
    const normalized = normalizeFieldKey(error.field);
    const mapped = mapBackendField(normalized);
    return {
      code: `server.${normalized || 'validation'}.${index}`,
      field: error.field,
      message: error.message,
      step: mapped.step,
      targetId: mapped.targetId,
      actionLabel: mapped.actionLabel,
    };
  });
}

function mapBackendField(field: string): Pick<JobValidationIssue, 'step' | 'targetId' | 'actionLabel'> {
  if (field.includes('reportnumber')) {
    return { step: 0, targetId: 'job-report-number', actionLabel: 'Ret sagsnummer' };
  }
  if (field.includes('destinationaddress')) {
    return { step: 0, targetId: 'job-destination-address', actionLabel: 'Ret destination' };
  }
  if (field.includes('customersnapshotname') || field === 'customername' || field.endsWith('customername')) {
    return { step: 0, targetId: 'job-customer-name', actionLabel: 'Ret kundenavn' };
  }
  if (field.includes('email')) {
    return { step: 0, targetId: 'job-customer-email', actionLabel: 'Ret e-mail' };
  }
  if (field.includes('phone')) {
    return { step: 0, targetId: 'job-customer-phone', actionLabel: 'Ret telefonnummer' };
  }
  if (field.includes('installationtype') || field.includes('categoryid') || field.includes('categoryids')) {
    return { step: 1, targetId: 'job-installation-types', actionLabel: 'Vælg anlægstype' };
  }
  if (field.includes('customworkkind')) {
    return { step: 1, targetId: 'job-custom-work-kind', actionLabel: 'Beskriv opgavetype' };
  }
  if (field.includes('workkind')) {
    return { step: 1, targetId: 'job-work-kind', actionLabel: 'Vælg opgavetype' };
  }
  if (field.includes('controlpoint') || field.includes('irrelevant') || field.includes('remarks')) {
    return { step: 2, targetId: 'job-control-points', actionLabel: 'Ret kontrolpunkter' };
  }
  if (field.includes('worksheet') || field.includes('timesheet')) {
    return { step: 3, targetId: 'job-worksheet-add-trigger', actionLabel: 'Tilføj timeseddel' };
  }
  if (field.includes('closureflag') || field.includes('closure')) {
    return { step: 4, targetId: 'job-closure-flags', actionLabel: 'Ret afslutningsstatus' };
  }

  return { step: 5, targetId: 'job-attestation-validation', actionLabel: 'Se fejlen' };
}

function normalizeFieldKey(field: string): string {
  return field
    .replace(/\[\d+\]/g, '')
    .replace(/[^a-zA-Z0-9]/g, '')
    .toLowerCase();
}

function capitalize(value: string): string {
  if (!value) return value;
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}
