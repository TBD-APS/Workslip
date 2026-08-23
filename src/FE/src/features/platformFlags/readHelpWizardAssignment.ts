import type { HelpWizardAssignment } from './evaluateHelpWizard';

const IDENTITY_KEY = 'workslip.flag.help-wizard';

function parseOptionalBoolean(value: string | undefined): boolean | null {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return null;
}

export function readHelpWizardAssignment(): HelpWizardAssignment {
  if (import.meta.env.VITE_HELP_WIZARD_KILL === 'true') {
    return { killed: true };
  }

  const identityRaw = typeof localStorage === 'undefined'
    ? null
    : localStorage.getItem(IDENTITY_KEY);

  return {
    application: parseOptionalBoolean(import.meta.env.VITE_HELP_WIZARD),
    identity: identityRaw === 'on' ? true : identityRaw === 'off' ? false : null,
  };
}
