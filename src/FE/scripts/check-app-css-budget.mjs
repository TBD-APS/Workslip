import { readFile } from 'node:fs/promises';
import process from 'node:process';

const APP_CSS_PATH = new URL('../src/App.css', import.meta.url);
// Ratchet, not a target: App.css is a legacy monolith being migrated away from,
// so this ceiling only ever moves down. Lower it to the measured size in the same
// change whenever a deletion reclaims bytes; never raise it to make room for new
// styling — that styling belongs in its owning feature/layout stylesheet.
const MAX_APP_CSS_BYTES = 103_767;

const source = await readFile(APP_CSS_PATH);

if (source.byteLength > MAX_APP_CSS_BYTES) {
  console.error(
    `App.css is ${source.byteLength} bytes, above the ${MAX_APP_CSS_BYTES}-byte migration ceiling. ` +
    'Move new styling into its owning feature/layout stylesheet instead of growing the legacy monolith.',
  );
  process.exit(1);
}

console.log(`App.css migration budget passed (${source.byteLength}/${MAX_APP_CSS_BYTES} bytes).`);
