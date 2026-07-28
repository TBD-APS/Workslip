import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const FONT_SOURCE_COMMIT = '9c76865650f64dd9242db524070753930bc7685c';
const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const outputDirectory = resolve(frontendRoot, 'public', 'fonts');

const fonts = [
  {
    name: 'Inter Variable',
    source: `https://raw.githubusercontent.com/fontsource/font-files/${FONT_SOURCE_COMMIT}/fonts/variable/inter/files/inter-latin-wght-normal.woff2`,
    filename: 'inter-latin-wght-normal-v5.3.0.woff2',
  },
  {
    name: 'Outfit Variable',
    source: `https://raw.githubusercontent.com/fontsource/font-files/${FONT_SOURCE_COMMIT}/fonts/variable/outfit/files/outfit-latin-wght-normal.woff2`,
    filename: 'outfit-latin-wght-normal-v5.3.0.woff2',
  },
];

const isWoff2 = (bytes) =>
  bytes.length > 10_000 &&
  bytes[0] === 0x77 &&
  bytes[1] === 0x4f &&
  bytes[2] === 0x46 &&
  bytes[3] === 0x32;

const hasValidFont = async (path) => {
  try {
    return isWoff2(await readFile(path));
  } catch {
    return false;
  }
};

const syncFont = async ({ name, source, filename }) => {
  const target = resolve(outputDirectory, filename);
  if (await hasValidFont(target)) {
    console.log(`[fonts] ${name} already present`);
    return;
  }

  const response = await fetch(source);
  if (!response.ok) {
    throw new Error(`[fonts] Failed to download ${name}: HTTP ${response.status}`);
  }

  const bytes = new Uint8Array(await response.arrayBuffer());
  if (!isWoff2(bytes)) {
    throw new Error(`[fonts] Downloaded ${name} is not a valid WOFF2 file`);
  }

  const temporaryTarget = `${target}.tmp`;
  try {
    await writeFile(temporaryTarget, bytes);
    await rename(temporaryTarget, target);
  } catch (error) {
    await rm(temporaryTarget, { force: true });
    throw error;
  }

  console.log(`[fonts] Downloaded ${name}`);
};

await mkdir(outputDirectory, { recursive: true });
await Promise.all(fonts.map(syncFont));
