/**
 * Export the authenticated Workslip palette as design tokens for Figma.
 *
 * `workslip-brand.css` is the active token layer for the authenticated app: it is
 * imported after the Farvelab layer and its `html body:has(...)` selectors win on
 * specificity. This script reads that file directly so the exported tokens cannot
 * drift from the palette the application actually renders.
 *
 * Usage: npm run export:design-tokens
 */

import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const brandPath = resolve(frontendRoot, 'src', 'workslip-brand.css');
const basePath = resolve(frontendRoot, 'src', 'farvelab-theme.css');
const outputPath = resolve(frontendRoot, 'design-tokens', 'workslip-tokens.json');

const NIGHT_SELECTOR = 'html body:has(.app-shell),';
const DAY_SELECTOR = 'html[data-theme="day"] body:has(.app-shell),';

/** Extract the declaration block that starts at the given selector. */
const blockAfter = (css, selectorStart) => {
  const index = css.indexOf(selectorStart);
  if (index === -1) throw new Error(`Selector not found: ${selectorStart}`);
  const open = css.indexOf('{', index);
  const close = css.indexOf('}', open);
  if (open === -1 || close === -1) throw new Error(`Malformed block for: ${selectorStart}`);
  return css.slice(open + 1, close);
};

const parseDeclarations = (block) => {
  const tokens = {};
  for (const line of block.split('\n')) {
    const match = /^\s*(--[a-z0-9-]+)\s*:\s*([^;]+);/i.exec(line);
    if (match) tokens[match[1]] = match[2].trim();
  }
  return tokens;
};

/**
 * Resolve `var(--brand-x)` indirection so Figma receives concrete values.
 *
 * The day block reuses the `--brand-*` values declared in the night block rather
 * than redeclaring them, so inherited declarations are needed to resolve it.
 */
const resolveAliases = (tokens, inherited = {}) => {
  const lookup = { ...inherited, ...tokens };
  const resolved = {};
  const aliases = {};
  for (const [name, value] of Object.entries(tokens)) {
    const alias = /^var\((--[a-z0-9-]+)\)$/i.exec(value);
    if (alias) {
      aliases[name] = alias[1];
      resolved[name] = lookup[alias[1]] ?? value;
    } else {
      resolved[name] = value;
    }
  }
  return { resolved, aliases };
};

/**
 * Figma variable metadata. `scope` keeps the Figma property picker clean and
 * `collection` mirrors the paired-collection layout the Figma file uses, because
 * Starter-plan collections are limited to a single mode each.
 */
const SCOPE_BY_PREFIX = [
  [/^--(text|on-primary)/, 'TEXT_FILL'],
  [/^--status-[a-z]+-text$/, 'TEXT_FILL'],
  [/^--(border|focus-ring)/, 'STROKE_COLOR'],
  [/^--(bg|surface|overlay|nav-bg|status-[a-z]+-bg|danger-bg)/, 'FRAME_FILL,SHAPE_FILL'],
  [/^--radius/, 'CORNER_RADIUS'],
];

const scopeFor = (name) => {
  for (const [pattern, scope] of SCOPE_BY_PREFIX) {
    if (pattern.test(name)) return scope.split(',');
  }
  return ['FRAME_FILL', 'SHAPE_FILL', 'TEXT_FILL', 'STROKE_COLOR'];
};

/** CSS custom property -> Figma variable name used in the Workslip Figma file. */
const FIGMA_NAME = {
  '--bg': 'bg',
  '--text': 'text/default',
  '--text-muted': 'text/muted',
  '--text-dim': 'text/dim',
  '--text-disabled': 'text/disabled',
  '--primary': 'primary/default',
  '--primary-hover': 'primary/hover',
  '--primary-pressed': 'primary/pressed',
  '--on-primary': 'primary/on',
  '--accent-cyan': 'accent/cyan',
  '--accent-mint': 'accent/mint',
  '--accent-amber': 'accent/amber',
  '--accent-coral': 'accent/coral',
  '--surface-floating': 'surface/floating',
  '--surface': 'surface/base',
  '--surface-raised': 'surface/raised',
  '--surface-input': 'surface/input',
  '--surface-elevated': 'surface/elevated',
  '--surface-selected': 'surface/selected',
  '--surface-selected-strong': 'surface/selected-strong',
  '--surface-modal': 'surface/modal',
  '--surface-overlay': 'surface/overlay',
  '--border': 'border/default',
  '--border-strong': 'border/strong',
  '--focus-ring': 'focus-ring',
  '--danger': 'status/danger',
  '--danger-hover': 'status/danger-hover',
  '--danger-bg': 'status/danger-bg',
  '--warning': 'status/warning',
  '--success': 'status/success',
  '--status-blue-bg': 'status/blue-bg',
  '--status-blue-text': 'status/blue-text',
  '--status-red-bg': 'status/red-bg',
  '--status-red-text': 'status/red-text',
  '--status-amber-bg': 'status/amber-bg',
  '--status-amber-text': 'status/amber-text',
  '--status-green-bg': 'status/green-bg',
  '--status-green-text': 'status/green-text',
  '--status-neutral-bg': 'status/neutral-bg',
  '--status-neutral-text': 'status/neutral-text',
  '--overlay-subtle': 'overlay/subtle',
  '--overlay-medium': 'overlay/medium',
  '--overlay-heavy': 'overlay/heavy',
  '--overlay-blur': 'overlay/blur',
  '--nav-bg': 'overlay/nav-bg',
};

const BRAND_NAMES = {
  '--brand-marine': 'brand/marine',
  '--brand-petrol': 'brand/petrol',
  '--brand-orange': 'brand/orange',
  '--brand-cream': 'brand/cream',
};

const toVariables = (themeTokens, aliases) => {
  const variables = [];
  for (const [cssName, figmaName] of Object.entries(FIGMA_NAME)) {
    const value = themeTokens[cssName];
    if (value === undefined) continue;
    const aliasOf = aliases[cssName];
    variables.push({
      name: figmaName,
      cssProperty: cssName,
      value,
      aliasOf: aliasOf ? BRAND_NAMES[aliasOf] ?? aliasOf : null,
      scopes: scopeFor(cssName),
      codeSyntax: { WEB: `var(${cssName})` },
    });
  }
  return variables;
};

const main = async () => {
  const brandCss = await readFile(brandPath, 'utf8');
  const farvelabCss = await readFile(basePath, 'utf8');

  const nightDeclarations = parseDeclarations(blockAfter(brandCss, NIGHT_SELECTOR));
  const night = resolveAliases(nightDeclarations);
  const day = resolveAliases(parseDeclarations(blockAfter(brandCss, DAY_SELECTOR)), nightDeclarations);
  const farvelab = parseDeclarations(blockAfter(farvelabCss, 'body:has(.app-shell) {'));

  const brand = {};
  for (const [cssName, figmaName] of Object.entries(BRAND_NAMES)) {
    if (night.resolved[cssName]) brand[figmaName] = night.resolved[cssName];
  }

  const scale = {};
  for (const name of ['--radius-sm', '--radius', '--radius-lg', '--card-padding']) {
    if (farvelab[name]) scale[name] = farvelab[name];
  }

  const document = {
    $schema: 'https://workslip.dk/schemas/design-tokens-1.json',
    meta: {
      generatedBy: 'src/FE/scripts/export-design-tokens.mjs',
      paletteSource: 'src/FE/src/workslip-brand.css',
      shapeSource: 'src/FE/src/farvelab-theme.css',
      note:
        'workslip-brand.css owns colour semantics and overrides the Farvelab layer on ' +
        'specificity. Farvelab still owns shape, spacing and component structure.',
      figma: {
        collections: ['Primitives', 'Color', 'Color Day', 'Scale'],
        modeLimitation:
          'Figma Starter allows one mode per collection, so Night and Day are paired ' +
          'collections sharing identical variable names. On a paid plan they merge into ' +
          'one collection with two modes without renaming anything.',
      },
    },
    collections: {
      Primitives: { modes: ['Value'], variables: brand },
      Color: { modes: ['Night'], variables: toVariables(night.resolved, night.aliases) },
      'Color Day': { modes: ['Day'], variables: toVariables(day.resolved, day.aliases) },
      Scale: { modes: ['Value'], variables: scale },
    },
  };

  await mkdir(dirname(outputPath), { recursive: true });
  await writeFile(outputPath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');

  const counts = {
    brand: Object.keys(brand).length,
    night: document.collections.Color.variables.length,
    day: document.collections['Color Day'].variables.length,
    scale: Object.keys(scale).length,
  };
  console.log(
    `[design-tokens] wrote ${outputPath}\n` +
      `[design-tokens] brand=${counts.brand} night=${counts.night} day=${counts.day} scale=${counts.scale}`,
  );

  const missing = Object.keys(FIGMA_NAME).filter((name) => night.resolved[name] === undefined);
  if (missing.length > 0) {
    console.warn(`[design-tokens] not present in the night theme: ${missing.join(', ')}`);
  }
};

await main();
