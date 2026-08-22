import { describe, expect, it } from 'vitest';
import { evaluateHelpWizard } from './evaluateHelpWizard';

describe('evaluateHelpWizard off-path', () => {
  it('stays off when unseeded', () => {
    expect(evaluateHelpWizard({})).toEqual({ enabled: false, source: 'default-off' });
  });

  it('kill beats identity on', () => {
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
});

describe('evaluateHelpWizard on-path', () => {
  it('application on enables when no narrower override', () => {
    expect(evaluateHelpWizard({ application: true })).toEqual({
      enabled: true,
      source: 'application',
    });
  });
});
