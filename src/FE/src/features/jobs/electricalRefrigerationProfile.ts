import type { ReferenceDataResponse, UserViewModel } from '../../api/generated/models';

export type ElectricalRefrigerationDiscipline = 'electrical' | 'refrigeration';

export type DemoCompetency = {
  discipline: ElectricalRefrigerationDiscipline;
  label: string;
  scope: string;
  validUntil: string;
};

const ELECTRICAL_LABEL = 'EL';
const REFRIGERATION_LABEL = 'KØL';

const DEMO_COMPETENCIES: Readonly<Record<string, readonly DemoCompetency[]>> = {
  'a1a1a1a1-da5b-4cc4-bbeb-07b40cab806f': [
    {
      discipline: 'electrical',
      label: 'Fagligt ansvarlig EL',
      scope: 'Installation og slutverifikation',
      validUntil: '2028-12-31',
    },
  ],
  'b2b2b2b2-da5b-4cc4-bbeb-07b40cab806f': [
    {
      discipline: 'refrigeration',
      label: 'KMO A2',
      scope: 'Køl, varmepumper og F-gas',
      validUntil: '2028-06-30',
    },
  ],
};

export function isElectricalRefrigerationProfile(referenceData: ReferenceDataResponse | null | undefined) {
  const labels = (referenceData?.installationTypes ?? [])
    .map((installationType) => installationType.name.trim().toLocaleUpperCase('da-DK'))
    .sort();

  return labels.length === 2
    && labels[0] === ELECTRICAL_LABEL
    && labels[1] === REFRIGERATION_LABEL;
}

export function getDisciplineInstallationIds(
  referenceData: ReferenceDataResponse | null | undefined,
  disciplines: readonly ElectricalRefrigerationDiscipline[],
) {
  const selectedLabels = new Set<string>(disciplines.map((discipline) => (
    discipline === 'electrical' ? ELECTRICAL_LABEL : REFRIGERATION_LABEL
  )));

  return (referenceData?.installationTypes ?? [])
    .filter((installationType) => selectedLabels.has(installationType.name.trim().toLocaleUpperCase('da-DK')))
    .map((installationType) => installationType.id);
}

export function getDemoCompetencies(userId: string): readonly DemoCompetency[] {
  return DEMO_COMPETENCIES[userId.toLowerCase()] ?? [];
}

export function getMissingDisciplines(
  selectedUsers: readonly Pick<UserViewModel, 'id'>[],
  disciplines: readonly ElectricalRefrigerationDiscipline[],
) {
  return disciplines.filter((discipline) => !selectedUsers.some((user) => (
    getDemoCompetencies(user.id).some((competency) => competency.discipline === discipline)
  )));
}

export function getDisciplineLabel(discipline: ElectricalRefrigerationDiscipline) {
  return discipline === 'electrical' ? ELECTRICAL_LABEL : REFRIGERATION_LABEL;
}
