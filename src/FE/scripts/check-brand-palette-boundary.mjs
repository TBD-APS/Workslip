import { readFile } from 'node:fs/promises';
import process from 'node:process';

const refinementPath = new URL('../src/farvelab-refinement.css', import.meta.url);
const brandPath = new URL('../src/workslip-brand.css', import.meta.url);
const appPath = new URL('../src/App.tsx', import.meta.url);
const themeProviderPath = new URL('../src/providers/ThemeProvider.tsx', import.meta.url);
const indexPath = new URL('../index.html', import.meta.url);
const activityFeedPath = new URL('../src/components/common/ActivityFeed.css', import.meta.url);
const quickNavigatorPath = new URL('../src/components/common/QuickNavigator.css', import.meta.url);

const [refinement, brand, app, themeProvider, index, activityFeed, quickNavigator] = await Promise.all([
  readFile(refinementPath, 'utf8'),
  readFile(brandPath, 'utf8'),
  readFile(appPath, 'utf8'),
  readFile(themeProviderPath, 'utf8'),
  readFile(indexPath, 'utf8'),
  readFile(activityFeedPath, 'utf8'),
  readFile(quickNavigatorPath, 'utf8'),
]);

const paletteTokens = [
  'bg',
  'text',
  'text-primary',
  'text-muted',
  'text-secondary',
  'text-dim',
  'primary',
  'primary-hover',
  'primary-pressed',
  'on-primary',
  'accent-cyan',
  'accent-mint',
  'accent-amber',
  'accent-coral',
  'surface-floating',
  'surface-color',
  'surface',
  'surface-raised',
  'surface-input',
  'surface-elevated',
  'surface-selected',
  'surface-selected-strong',
  'surface-modal',
  'surface-actions-menu',
  'border',
  'border-strong',
  'focus-ring',
  'nav-bg',
  'safe-area-top-bg',
];

const violations = paletteTokens.filter((token) => {
  const pattern = new RegExp(`(^|\\n)\\s*--${token}\\s*:`, 'm');
  return pattern.test(refinement);
});

if (violations.length > 0) {
  console.error(
    `farvelab-refinement.css redeclares brand palette tokens owned by workslip-brand.css: ${violations.join(', ')}`,
  );
  process.exit(1);
}

for (const expected of ['#123b4a', '#147a7e', '#f47a24', '#fff7e8']) {
  if (!brand.toLowerCase().includes(expected)) {
    console.error(`workslip-brand.css is missing canonical brand color ${expected}.`);
    process.exit(1);
  }
}

if (!app.includes("import './workslip-brand.css';")) {
  console.error('App.tsx must load workslip-brand.css so the canonical palette reaches every supported shell.');
  process.exit(1);
}

for (const requiredScope of ['body:has(.app-shell)', 'body:has(.auth-shell)', 'body:has(.system-state)']) {
  if (!brand.includes(requiredScope)) {
    console.error(`workslip-brand.css is missing required semantic scope ${requiredScope}.`);
    process.exit(1);
  }
}

const nonActionSharedSurfaces = [
  ['ActivityFeed.css', activityFeed],
  ['QuickNavigator.css', quickNavigator],
];
for (const [fileName, source] of nonActionSharedSurfaces) {
  if (source.includes('var(--primary)')) {
    console.error(`${fileName} uses --primary for non-action state. Use --color-primary/--color-info or --focus-ring instead.`);
    process.exit(1);
  }
  if (/#(?:2563eb|1d4ed8|3b82f6|00c6ff)\b/i.test(source)) {
    console.error(`${fileName} contains a legacy blue/cyan state literal. Consume the central semantic tokens instead.`);
    process.exit(1);
  }
}

for (const [fileName, source] of [
  ['ThemeProvider.tsx', themeProvider],
  ['index.html', index],
]) {
  for (const expected of ['#123B4A', '#FFF7E8']) {
    if (!source.includes(expected)) {
      console.error(`${fileName} is not aligned with canonical browser theme color ${expected}.`);
      process.exit(1);
    }
  }
}

console.log('Workslip brand palette ownership and shared state semantics guard passed.');
