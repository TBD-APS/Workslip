#!/usr/bin/env node
/**
 * depmap — Workslip module dependency & coupling mapper (WOR-443).
 *
 * Scans backend C# namespaces/usings and frontend TypeScript imports, then
 * emits a module-level dependency map with coupling metrics so boundary
 * splits can be prioritized and their effect measured over time.
 *
 * Usage:
 *   node tools/depmap/depmap.mjs            # writes Docs/architecture/dependency-map.md + tools/depmap/last-run.json
 *   node tools/depmap/depmap.mjs --check    # exits non-zero if the committed markdown is stale
 *
 * The tool is read-only with respect to source code and has no dependencies.
 */

import { readdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join, relative, resolve, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const BE_ROOT = join(ROOT, 'src', 'BE', 'WorkslipApi');
const FE_SRC = join(ROOT, 'src', 'FE', 'src');
const MD_OUT = join(ROOT, 'Docs', 'architecture', 'dependency-map.md');
const JSON_OUT = join(ROOT, 'tools', 'depmap', 'last-run.json');

const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', 'dist', '.git', 'Workslip.Tests']);
const GOD_FILE_COUNT = 15;

// ---------- generic helpers ----------

function walk(dir, exts, out = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) walk(join(dir, entry.name), exts, out);
    } else if (exts.some((e) => entry.name.endsWith(e))) {
      out.push(join(dir, entry.name));
    }
  }
  return out;
}

const countLines = (text) => (text.length === 0 ? 0 : text.split('\n').length);
const rel = (p) => relative(ROOT, p).split(sep).join('/');
const sortDesc = (arr, key) => [...arr].sort((a, b) => b[key] - a[key] || String(a.name ?? a.file).localeCompare(String(b.name ?? b.file)));

// ---------- backend scan ----------

function backendModule(namespace) {
  // Workslip.Application.Jobs.X -> { layer: 'Application', module: 'Jobs' }
  const parts = namespace.split('.');
  if (parts[0] !== 'Workslip') return { layer: 'Api', module: parts[0] };
  const layer = parts[1] ?? 'Root';
  if (layer === 'Domain') return { layer: 'Domain', module: 'Domain' };
  return { layer, module: parts[2] ?? '(root)' };
}

function scanBackend() {
  const files = walk(BE_ROOT, ['.cs']);
  const modules = new Map(); // "Layer/Module" -> { files, loc }
  const edges = new Map(); // "fromKey -> toKey" -> fileRefCount
  const godFiles = [];

  for (const file of files) {
    const text = readFileSync(file, 'utf8');
    const nsMatch = text.match(/^\s*namespace\s+([\w.]+)/m);
    if (!nsMatch) continue;
    const from = backendModule(nsMatch[1]);
    const fromKey = `${from.layer}/${from.module}`;
    const loc = countLines(text);

    const mod = modules.get(fromKey) ?? { files: 0, loc: 0 };
    mod.files += 1;
    mod.loc += loc;
    modules.set(fromKey, mod);
    godFiles.push({ file: rel(file), loc });

    const targets = new Set();
    for (const re of [
      /^\s*using\s+(?:static\s+)?(Workslip\.[\w.]+)\s*;/gm,
      /^\s*using\s+\w+\s*=\s*(Workslip\.[\w.]+)\s*;/gm,
    ]) {
      for (const m of text.matchAll(re)) {
        const to = backendModule(m[1]);
        const toKey = `${to.layer}/${to.module}`;
        if (toKey !== fromKey) targets.add(toKey);
      }
    }
    for (const toKey of targets) {
      const edgeKey = `${fromKey} -> ${toKey}`;
      edges.set(edgeKey, (edges.get(edgeKey) ?? 0) + 1);
    }
  }

  return { modules, edges, godFiles: sortDesc(godFiles, 'loc').slice(0, GOD_FILE_COUNT) };
}

function backendMetrics({ modules, edges }) {
  // Coupling metrics for Application modules only: Domain is the shared kernel
  // and Infrastructure implements Application ports, so cross-module coupling
  // inside Application is the split-relevant signal.
  const appModules = [...modules.keys()].filter((k) => k.startsWith('Application/') && k !== 'Application/(root)');
  const rows = [];
  for (const key of appModules) {
    let fanOut = 0;
    let fanIn = 0;
    let outRefs = 0;
    let inRefs = 0;
    for (const [edge, count] of edges) {
      const [from, to] = edge.split(' -> ');
      if (from === key && appModules.includes(to)) { fanOut += 1; outRefs += count; }
      if (to === key && appModules.includes(from)) { fanIn += 1; inRefs += count; }
    }
    const { files, loc } = modules.get(key);
    rows.push({ name: key.slice('Application/'.length), files, loc, fanIn, fanOut, inRefs, outRefs, coupling: fanIn + fanOut });
  }
  return sortDesc(rows, 'coupling');
}

// ---------- frontend scan ----------

const SHARED_BUCKETS = new Set(['lib', 'hooks', 'providers', 'components', 'api', 'types', 'routes', 'telemetry']);
const isTestFile = (p) => /\.test\.|__tests__|\/test\//.test(p);

function feAreaOf(absPath) {
  const parts = relative(FE_SRC, absPath).split(sep);
  if (parts[0] === 'features') return { kind: 'feature', name: parts[1] };
  if (SHARED_BUCKETS.has(parts[0])) return { kind: 'shared', name: parts[0] };
  return { kind: 'other', name: parts[0] };
}

function scanFrontend() {
  const files = walk(FE_SRC, ['.ts', '.tsx']).filter((f) => !isTestFile(rel(f)) && !rel(f).includes('api/generated'));
  const features = new Map(); // name -> { files, loc, sharedRefs, crossRefs }
  const crossEdges = new Map(); // "featureA -> featureB" -> count
  const godFiles = [];
  const importRe = /(?:import|export)\s[^'"]*?from\s*['"]([^'"]+)['"]|import\s*\(\s*['"]([^'"]+)['"]\s*\)|^\s*import\s+['"]([^'"]+)['"]/gm;

  for (const file of files) {
    const area = feAreaOf(file);
    const text = readFileSync(file, 'utf8');
    const loc = countLines(text);
    godFiles.push({ file: rel(file), loc });
    if (area.kind !== 'feature') continue;

    const feat = features.get(area.name) ?? { files: 0, loc: 0, sharedRefs: 0, crossRefs: 0 };
    feat.files += 1;
    feat.loc += loc;

    for (const m of text.matchAll(importRe)) {
      const spec = m[1] ?? m[2] ?? m[3];
      if (!spec || !spec.startsWith('.')) continue; // package imports are not coupling signals here
      const target = resolve(dirname(file), spec);
      if (!target.startsWith(FE_SRC)) continue;
      const targetArea = feAreaOf(target);
      if (targetArea.kind === 'shared') feat.sharedRefs += 1;
      if (targetArea.kind === 'feature' && targetArea.name !== area.name) {
        feat.crossRefs += 1;
        const edgeKey = `${area.name} -> ${targetArea.name}`;
        crossEdges.set(edgeKey, (crossEdges.get(edgeKey) ?? 0) + 1);
      }
    }
    features.set(area.name, feat);
  }

  return {
    features: sortDesc([...features.entries()].map(([name, v]) => ({ name, ...v })), 'crossRefs'),
    crossEdges,
    godFiles: sortDesc(godFiles, 'loc').slice(0, GOD_FILE_COUNT),
  };
}

// ---------- report ----------

function mdTable(headers, rows) {
  return [
    `| ${headers.join(' | ')} |`,
    `| ${headers.map(() => '---').join(' | ')} |`,
    ...rows.map((r) => `| ${r.join(' | ')} |`),
  ].join('\n');
}

function buildReport(be, beRows, fe) {
  const appEdges = [...be.edges.entries()]
    .filter(([k]) => {
      const [from, to] = k.split(' -> ');
      return from.startsWith('Application/') && to.startsWith('Application/');
    })
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const infraEdges = [...be.edges.entries()]
    .filter(([k]) => k.startsWith('Infrastructure/') && k.includes('-> Application/'))
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const feCross = [...fe.crossEdges.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  return `# Dependency map

**Status:** Generated — do not edit by hand
**Source:** \`node tools/depmap/depmap.mjs\` (verify freshness with \`--check\`)
**Purpose:** Module-level dependency and coupling map for boundary-split work ([WOR-443](https://linear.app/workslip/issue/WOR-443)). Regenerate after each split to confirm coupling actually went down.

## Backend — Application module coupling

Coupling = fan-in + fan-out between \`Workslip.Application.*\` modules. File refs = number of files importing across the boundary. Domain is the shared kernel and is excluded; Infrastructure implements Application ports and is listed separately.

${mdTable(
    ['Module', 'Files', 'LOC', 'Fan-in', 'Fan-out', 'Inbound file refs', 'Outbound file refs', 'Coupling'],
    beRows.map((r) => [r.name, r.files, r.loc, r.fanIn, r.fanOut, r.inRefs, r.outRefs, `**${r.coupling}**`]),
  )}

### Cross-module edges (Application → Application)

${mdTable(['From → To', 'File refs'], appEdges.map(([k, v]) => [k.replaceAll('Application/', ''), v]))}

### Infrastructure → Application references

${mdTable(['From → To', 'File refs'], infraEdges.map(([k, v]) => [k.replace('Infrastructure/', 'Infra:').replace('Application/', 'App:'), v]))}

## Frontend — feature isolation

Cross-feature imports are boundary violations; shared refs (\`lib/\`, \`hooks/\`, \`providers/\`, …) are the sanctioned coupling path. Test files and \`api/generated\` are excluded.

${mdTable(
    ['Feature', 'Files', 'LOC', 'Cross-feature imports', 'Shared imports'],
    fe.features.map((f) => [f.name, f.files, f.loc, f.crossRefs === 0 ? '0' : `**${f.crossRefs}**`, f.sharedRefs]),
  )}

${feCross.length === 0 ? '_No cross-feature imports detected._' : `### Cross-feature edges\n\n${mdTable(['From → To', 'Imports'], feCross.map(([k, v]) => [k, v]))}`}

## God-file watchlist (largest files)

### Backend

${mdTable(['File', 'LOC'], be.godFiles.map((f) => [`\`${f.file}\``, f.loc]))}

### Frontend

${mdTable(['File', 'LOC'], fe.godFiles.map((f) => [`\`${f.file}\``, f.loc]))}

## Method

- Backend: each \`.cs\` file is assigned to a module by its declared namespace (\`Workslip.<Layer>.<Module>\`). Edges are distinct \`using Workslip.*\` targets per file, aggregated per module pair. \`bin\`, \`obj\` and \`Workslip.Tests\` are excluded.
- Frontend: each \`.ts/.tsx\` under \`src/FE/src/features/<feature>\` is scanned for relative imports; targets are classified as same-feature, cross-feature or shared.
- Namespace/using parsing is text-based, not Roslyn-based: fully-qualified type references without a \`using\` are not counted. Treat numbers as a consistent lower bound, good for trends, not an exhaustive census.
`;
}

// ---------- main ----------

const be = scanBackend();
const beRows = backendMetrics(be);
const fe = scanFrontend();
const report = buildReport(be, beRows, fe);

const json = {
  backend: {
    modules: Object.fromEntries([...be.modules.entries()].sort((a, b) => a[0].localeCompare(b[0]))),
    edges: Object.fromEntries([...be.edges.entries()].sort((a, b) => a[0].localeCompare(b[0]))),
    applicationCoupling: beRows,
    godFiles: be.godFiles,
  },
  frontend: {
    features: fe.features,
    crossFeatureEdges: Object.fromEntries([...fe.crossEdges.entries()].sort((a, b) => a[0].localeCompare(b[0]))),
    godFiles: fe.godFiles,
  },
};

if (process.argv.includes('--check')) {
  const current = existsSync(MD_OUT) ? readFileSync(MD_OUT, 'utf8') : '';
  if (current !== report) {
    console.error('dependency-map.md is stale. Run: node tools/depmap/depmap.mjs');
    process.exit(1);
  }
  console.log('dependency-map.md is up to date.');
} else {
  writeFileSync(MD_OUT, report);
  writeFileSync(JSON_OUT, JSON.stringify(json, null, 2) + '\n');
  console.log(`Wrote ${rel(MD_OUT)} and ${rel(JSON_OUT)}`);
}
