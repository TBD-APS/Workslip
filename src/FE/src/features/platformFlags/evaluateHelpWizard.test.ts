import { describe, expect, it } from 'vitest';
import { evaluateHelpWizard } from './evaluateHelpWizard';

describe('evaluateHelpWizard off-path', () => {
  it('kill beats explicit identity on', () => {
    expect(evaluateHelpWizard({ killed: true, identity: true })).toEqual({
      enabled: false,
      source: 'platform-kill',
    });
  });

  it('identity can turn application off', () => {
    expect(evaluateHelpWizard({ application: true, identity: false })).toEqual({
      enabled: false,
      source: 'identity',
    });
  });

  it('application can explicitly keep the wizard off', () => {
    expect(evaluateHelpWizard({ application: false })).toEqual({
      enabled: false,
      source: 'application',
    });
  });

  it('fails closed when no assignment exists', () => {
    expect(evaluateHelpWizard({})).toEqual({ enabled: false, source: 'default-off' });
  });
});

describe('evaluateHelpWizard on-path', () => {
  it('application on enables when no narrower override', () => {
    expect(evaluateHelpWizard({ application: true })).toEqual({
      enabled: true,
      source: 'application',
    });
  });

  it('tenant on enables when identity is unset', () => {
    expect(evaluateHelpWizard({ tenant: true, application: false })).toEqual({
      enabled: true,
      source: 'tenant',
    });
  });
});
