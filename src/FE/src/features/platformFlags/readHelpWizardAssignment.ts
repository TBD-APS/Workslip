import type { HelpWizardAssignment } from './evaluateHelpWizard';

const IDENTITY_KEY = 'workslip.flag.help-wizard';

export function readHelpWizardAssignment(): HelpWizardAssignment {
  if (import.meta.env.VITE_HELP_WIZARD_KILL === 'true') {
    return { killed: true };
  }

  const identityRaw = typeof localStorage === 'undefined'
    ? null
    : localStorage.getItem(IDENTITY_KEY);

  return {
    application: import.meta.env.VITE_HELP_WIZARD === 'true',
    identity: identityRaw === 'on' ? true : identityRaw === 'off' ? false : null,
  };
}
