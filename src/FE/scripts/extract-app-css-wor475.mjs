import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const srcDir = path.resolve(scriptDir, '..', 'src');
const appCssPath = path.join(srcDir, 'App.css');
const formCssPath = path.join(srcDir, 'components', 'forms', 'FormPrimitives.css');
const shellCssPath = path.join(srcDir, 'components', 'layouts', 'AppLayout.shell.css');
const profileCssPath = path.join(srcDir, 'features', 'settings', 'routes', 'Profile.css');
const authenticatedBasePath = path.join(srcDir, 'authenticated-base.css');
const budgetPath = path.join(scriptDir, 'check-app-css-budget.mjs');

let appCss = await readFile(appCssPath, 'utf8');

function take(pattern, label) {
  const match = appCss.match(pattern);
  if (!match) throw new Error(`WOR-475 extraction could not find ${label}.`);
  appCss = appCss.replace(match[0], '');
  return match[0].trim();
}

const forms = take(/\/\* Forms \*\/[\s\S]*?(?=\n\n\.user-avatar \{)/, 'shared form primitives');
const avatar = take(/\.user-avatar \{[\s\S]*?(?=\n\.profile-edit-actions \{)/, 'app-shell avatar controls');
const profileActions = take(/\.profile-edit-actions \{[\s\S]*?(?=\n\n\/\* Job List Components \*\/)/, 'profile edit actions');

appCss = appCss
  .replace('/* App Shell & Forms CSS */\n\n', '')
  .replace(/\n{4,}/g, '\n\n\n');

await writeFile(formCssPath, `@charset "UTF-8";\n\n/* Shared authenticated form primitives extracted from legacy App.css by WOR-475. */\n${forms}\n`);

let shellCss = await readFile(shellCssPath, 'utf8');
if (!shellCss.includes('.user-avatar {')) {
  shellCss = `${shellCss.trimEnd()}\n\n/* Header/account controls */\n${avatar}\n`;
  await writeFile(shellCssPath, shellCss);
}

let profileCss = await readFile(profileCssPath, 'utf8');
if (!profileCss.includes('.profile-edit-actions {')) {
  profileCss = `${profileCss.trimEnd()}\n\n/* Profile edit actions */\n${profileActions}\n`;
  await writeFile(profileCssPath, profileCss);
}

let authenticatedBase = await readFile(authenticatedBasePath, 'utf8');
const formImport = "@import './components/forms/FormPrimitives.css';";
if (!authenticatedBase.includes(formImport)) {
  authenticatedBase = authenticatedBase.replace('@charset "UTF-8";\n', `@charset "UTF-8";\n\n${formImport}\n`);
  await writeFile(authenticatedBasePath, authenticatedBase);
}

await writeFile(appCssPath, appCss);

const appCssBytes = Buffer.byteLength(appCss, 'utf8');
const ceiling = Math.ceil(appCssBytes / 1000) * 1000;
let budget = await readFile(budgetPath, 'utf8');
budget = budget.replace(/const MAX_APP_CSS_BYTES = [\d_]+;/, `const MAX_APP_CSS_BYTES = ${String(ceiling).replace(/\B(?=(\d{3})+(?!\d))/g, '_')};`);
await writeFile(budgetPath, budget);

console.log(`WOR-475 extracted shared forms, app-shell account controls and profile actions. App.css is now ${appCssBytes} bytes; ceiling ${ceiling}.`);
