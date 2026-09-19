import { describe, expect, it } from 'vitest';
import type { ReferenceDataResponse, UserViewModel } from '../../api/generated/models';
import {
  getDemoCompetencies,
  getDisciplineInstallationIds,
  getMissingDisciplines,
  isElectricalRefrigerationProfile,
} from './electricalRefrigerationProfile';

function referenceData(...types: Array<{ id: string; name: string }>): ReferenceDataResponse {
  return {
    installationTypes: types.map((type, index) => ({
      ...type,
      sortOrder: index + 1,
      categories: [],
    })),
    workKinds: [],
    closureFlags: [],
  };
}

function user(id: string): UserViewModel {
  return {
    id,
    organizationId: 'organization-1',
    email: `${id}@example.invalid`,
    displayName: id,
    phone: '',
    role: 'User',
    roleDisplayName: 'Medarbejder',
    hoursThisWeek: null,
    hoursThisMonth: null,
    hoursBiweekly: null,
  };
}

describe('electricalRefrigerationProfile', () => {
  it('activates only for the exact EL and KØL catalogue', () => {
    expect(isElectricalRefrigerationProfile(referenceData(
      { id: 'el', name: 'EL' },
      { id: 'koel', name: 'KØL' },
    ))).toBe(true);

    expect(isElectricalRefrigerationProfile(referenceData(
      { id: 'vand', name: 'Vand' },
      { id: 'afloeb', name: 'Afløb' },
    ))).toBe(false);

    expect(isElectricalRefrigerationProfile(referenceData(
      { id: 'el', name: 'EL' },
      { id: 'koel', name: 'KØL' },
      { id: 'vand', name: 'Vand' },
    ))).toBe(false);
  });

  it('resolves only installation ids for the selected disciplines', () => {
    const data = referenceData(
      { id: 'el-id', name: 'EL' },
      { id: 'koel-id', name: 'KØL' },
    );

    expect(getDisciplineInstallationIds(data, ['electrical'])).toEqual(['el-id']);
    expect(getDisciplineInstallationIds(data, ['refrigeration'])).toEqual(['koel-id']);
    expect(getDisciplineInstallationIds(data, ['electrical', 'refrigeration'])).toEqual(['el-id', 'koel-id']);
  });

  it('requires selected employees to cover every active discipline', () => {
    const electricalEmployee = user('A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F');
    const refrigerationEmployee = user('B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F');

    expect(getDemoCompetencies(electricalEmployee.id)).toHaveLength(1);
    expect(getMissingDisciplines([electricalEmployee], ['electrical', 'refrigeration']))
      .toEqual(['refrigeration']);
    expect(getMissingDisciplines(
      [electricalEmployee, refrigerationEmployee],
      ['electrical', 'refrigeration'],
    )).toEqual([]);
  });
});
