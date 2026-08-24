/** Mirrors MR SAAS'y platform.help-wizard. Default off / fail closed. Kill wins. */

export const PLATFORM_HELP_WIZARD = 'platform.help-wizard';

export type FeatureFlagSource =
  | 'platform-kill'
  | 'identity'
  | 'tenant'
  | 'application'
  | 'default-off';

export type HelpWizardAssignment = {
  killed?: boolean;
  identity?: boolean | null;
  tenant?: boolean | null;
  application?: boolean | null;
};

export function evaluateHelpWizard(assignment: HelpWizardAssignment): {
  enabled: boolean;
  source: FeatureFlagSource;
} {
  if (assignment.killed) {
    return { enabled: false, source: 'platform-kill' };
  }

  if (assignment.identity === false) {
    return { enabled: false, source: 'identity' };
  }

  if (assignment.identity === true) {
    return { enabled: true, source: 'identity' };
  }

  if (assignment.tenant === false) {
    return { enabled: false, source: 'tenant' };
  }

  if (assignment.tenant === true) {
    return { enabled: true, source: 'tenant' };
  }

  if (assignment.application === false) {
    return { enabled: false, source: 'application' };
  }

  if (assignment.application === true) {
    return { enabled: true, source: 'application' };
  }

  return { enabled: false, source: 'default-off' };
}
