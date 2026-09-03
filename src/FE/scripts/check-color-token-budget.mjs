/**
 * Shrinking ceiling for colour literals that bypass the token layer.
 *
 * `workslip-brand.css` owns colour semantics for the authenticated app. Every
 * colour written directly into an ordinary declaration is a value that cannot
 * follow the day/night themes and cannot be changed in one place.
 *
 * This guard counts those literals across the frontend stylesheets. Values that
 * define a custom property are excluded: that is the token layer doing its job.
 *
 * Like the App.css byte budget, this is a ceiling and not a target. Some
 * literals are legitimate — transparent gradient stops, neutral scrims. Lower
 * the ceiling in the same change whenever a safe conversion removes some.
 */

import { readdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import process from 'node:process';

const STYLESHEET_ROOT = fileURLToPath(new URL('../src', import.meta.url));

// Lower this whenever a change converts literals to tokens. Never raise it.
const MAX_UNTOKENISED_COLOURS = 108;

const COLOUR = /#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?)\([^)]*\)/gi;

/** Strip comments and url() payloads so neither can contribute false matches. */
const clean = (css) => css.replace(/\/\*[\s\S]*?\*\//g, '').replace(/url\([^)]*\)/gi, 'url()');

const countFile = (css) =>
  clean(css)
    .split(';')
    // A declaration that defines a custom property is the token layer itself.
    .filter((declaration) => !/^\s*--[a-z0-9-]+\s*:/i.test(declaration))
    .reduce((total, declaration) => total + (declaration.match(COLOUR) ?? []).length, 0);

const entries = await readdir(STYLESHEET_ROOT, { recursive: true });
const stylesheets = entries.filter((entry) => entry.endsWith('.css')).sort();

const perFile = [];
let total = 0;
for (const relative of stylesheets) {
  const count = countFile(await readFile(resolve(STYLESHEET_ROOT, relative), 'utf8'));
  if (count > 0) perFile.push([relative, count]);
  total += count;
}

if (total > MAX_UNTOKENISED_COLOURS) {
  const worst = perFile
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5)
    .map(([file, count]) => `  ${count}\t${file}`)
    .join('\n');
  console.error(
    `${total} colour literals sit outside the token layer, above the ${MAX_UNTOKENISED_COLOURS} ceiling.\n` +
      'Consume a semantic token from workslip-brand.css instead of writing a colour directly.\n' +
      `Largest contributors:\n${worst}`,
  );
  process.exit(1);
}

console.log(
  `Colour token budget passed (${total}/${MAX_UNTOKENISED_COLOURS} literals outside the token layer).`,
);
