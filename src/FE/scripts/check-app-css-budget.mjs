import { readFile } from 'node:fs/promises';
import process from 'node:process';

const APP_CSS_PATH = new URL('../src/App.css', import.meta.url);
const MAX_APP_CSS_BYTES = 130_000;

const source = await readFile(APP_CSS_PATH);

if (source.byteLength > MAX_APP_CSS_BYTES) {
  console.error(
    `App.css is ${source.byteLength} bytes, above the ${MAX_APP_CSS_BYTES}-byte migration ceiling. ` +
    'Move new styling into its owning feature/layout stylesheet instead of growing the legacy monolith.',
  );
  process.exit(1);
}

console.log(`App.css migration budget passed (${source.byteLength}/${MAX_APP_CSS_BYTES} bytes).`);
