export type DomainFieldKey =
  | 'customer.name'
  | 'customer.number'
  | 'customer.phone'
  | 'customer.email'
  | 'customer.contactPerson'
  | 'customer.jobCount'
  | 'address.full'
  | 'user.name'
  | 'user.phone'
  | 'user.email'
  | 'user.role'
  | 'job.reportNumber'
  | 'job.status'
  | 'job.type'
  | 'job.taskDescription'
  | 'job.updatedAt'
  | 'job.totalHours';

export type DomainFieldAction = 'copy' | 'call' | 'email' | 'maps';

export type DomainFieldPolicy = {
  copyable: boolean;
  actions: readonly DomainFieldAction[];
  label: string;
  normalize: (value: string) => string;
};

const trim = (value: string) => value.trim();
const trimAndCollapseWhitespace = (value: string) => value.trim().replace(/\s+/g, ' ');

/**
 * Canonical UI interaction policy for domain values.
 *
 * Every policy-controlled domain value must be declared here with explicit
 * copyability and available actions. UI renderers must consult this registry
 * rather than introducing page-local clipboard, tel:, mailto: or Maps logic.
 * Changing a field here is intended to change its behaviour everywhere that
 * field is rendered through the shared policy-aware value component.
 *
 * Keep copyable explicit even though `copy` also appears in actions: it is the
 * product-level copyability decision reviewers can audit at a glance. Tests
 * enforce that copyable === actions.includes('copy').
 */
export const domainFieldPolicyRegistry: Record<DomainFieldKey, DomainFieldPolicy> = {
  'customer.name': { copyable: true, actions: ['copy'], label: 'Kundenavn', normalize: trimAndCollapseWhitespace },
  'customer.number': { copyable: true, actions: ['copy'], label: 'Kundenummer', normalize: trim },
  'customer.phone': { copyable: true, actions: ['copy', 'call'], label: 'Telefonnummer', normalize: trim },
  'customer.email': { copyable: true, actions: ['copy', 'email'], label: 'E-mail', normalize: trim },
  'customer.contactPerson': { copyable: true, actions: ['copy'], label: 'Kontaktperson', normalize: trimAndCollapseWhitespace },
  'customer.jobCount': { copyable: false, actions: [], label: 'Antal sager', normalize: trim },
  'address.full': { copyable: true, actions: ['copy'], label: 'Adresse', normalize: trimAndCollapseWhitespace },
  'user.name': { copyable: true, actions: ['copy'], label: 'Medarbejdernavn', normalize: trimAndCollapseWhitespace },
  'user.phone': { copyable: true, actions: ['copy', 'call'], label: 'Telefonnummer', normalize: trim },
  'user.email': { copyable: true, actions: ['copy', 'email'], label: 'E-mail', normalize: trim },
  'user.role': { copyable: false, actions: [], label: 'Rolle', normalize: trimAndCollapseWhitespace },
  'job.reportNumber': { copyable: true, actions: ['copy'], label: 'Sagsnummer', normalize: trim },
  'job.status': { copyable: false, actions: [], label: 'Sagsstatus', normalize: trimAndCollapseWhitespace },
  'job.type': { copyable: false, actions: [], label: 'Sagstype', normalize: trimAndCollapseWhitespace },
  'job.taskDescription': { copyable: false, actions: [], label: 'Opgavebeskrivelse', normalize: trimAndCollapseWhitespace },
  'job.updatedAt': { copyable: false, actions: [], label: 'Opdateringstidspunkt', normalize: trim },
  'job.totalHours': { copyable: false, actions: [], label: 'Timer', normalize: trim },
};

// Backwards-compatible name while existing call sites migrate to DomainValue terminology.
export const copyableFieldRegistry = domainFieldPolicyRegistry;
export type CopyableFieldKey = DomainFieldKey;

export function getDomainFieldPolicy(field: DomainFieldKey): DomainFieldPolicy {
  return domainFieldPolicyRegistry[field];
}

export const getCopyableFieldPolicy = getDomainFieldPolicy;

export function normalizeDomainValue(
  field: DomainFieldKey,
  value: string | null | undefined,
): string {
  if (!value) return '';
  return getDomainFieldPolicy(field).normalize(value);
}

export const normalizeCopyableValue = normalizeDomainValue;

export function getCopySuccessMessage(field: DomainFieldKey): string {
  return `${getDomainFieldPolicy(field).label} kopieret`;
}

export function getCallHref(value: string): string {
  return `tel:${value.replace(/[^+\d]/g, '')}`;
}

export function getEmailHref(value: string): string {
  return `mailto:${value.trim()}`;
}
