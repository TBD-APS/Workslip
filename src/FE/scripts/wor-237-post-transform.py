from pathlib import Path

root = Path(__file__).resolve().parents[3]
source_path = root / 'src/FE/src/features/superadmin/organizationSession.ts'
content = source_path.read_text(encoding='utf-8')

old_import = """import {
  AUTH_TOKEN_KEY,
  AuthStorage,
  REAUTH_IN_FLIGHT_KEY,
} from '../../providers/authContextValue';
"""
new_import = """import {
  AUTH_TOKEN_KEY,
  AuthStorage,
  REAUTH_IN_FLIGHT_KEY,
  USER_EMAIL_KEY,
} from '../../providers/authContextValue';
"""
if old_import not in content:
    raise SystemExit('Transformed organization-session import was not found')
content = content.replace(old_import, new_import, 1)

helper = """
function clearAuthenticationAndOrganizationSession(): void {
  // Remove the potentially customer-scoped credential before its metadata.
  AuthStorage.removeItem(AUTH_TOKEN_KEY);
  AuthStorage.removeItem(USER_EMAIL_KEY);
  AuthStorage.removeItem(REAUTH_IN_FLIGHT_KEY);
  clearOrganizationSession();
}
"""
if 'function clearAuthenticationAndOrganizationSession()' not in content:
    content = content.rstrip() + '\n\n' + helper.lstrip()

source_path.write_text(content, encoding='utf-8')
