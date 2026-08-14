// Generates the Orval API client from the backend OpenAPI contract in this
// working tree. The document is produced by an isolated build-time generation
// pass, so local generation needs neither a running API process nor a database.
// The same script backs the CI action, keeping local and CI contracts identical.

import { spawn } from 'node:child_process';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(frontendRoot, '..', '..');
const apiProject = resolve(repositoryRoot, 'src/BE/WorkslipApi/Workslip.Api.csproj');

const skipRestore = process.argv.includes('--no-restore');

const run = (command, args, options = {}) =>
  new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, args, { stdio: 'inherit', ...options });

    child.on('error', (cause) => {
      if (cause.code === 'ENOENT') {
        rejectPromise(new Error(`${command} was not found on PATH.`, { cause }));
        return;
      }
      rejectPromise(cause);
    });

    child.on('close', (code, signal) => {
      if (code === 0) {
        resolvePromise();
        return;
      }
      rejectPromise(new Error(`${command} exited with ${signal ?? `code ${code}`}.`));
    });
  });

const buildOpenApiDocument = async (outputDirectory) => {
  console.log('[api] building backend OpenAPI document');

  await run(
    'dotnet',
    [
      'build',
      apiProject,
      '--configuration',
      'Release',
      ...(skipRestore ? ['--no-restore'] : []),
      '--nologo',
      '-p:OpenApiGenerateDocuments=true',
      `-p:OpenApiDocumentsDirectory=${outputDirectory}`,
    ],
    {
      cwd: repositoryRoot,
      // Contract inspection must not resolve database services, alter schema,
      // seed data or start database-backed workers.
      env: { ...process.env, Workslip__GenerateOpenApiOnly: 'true' },
    },
  );

  const documents = (await readdir(outputDirectory)).filter((entry) => entry.endsWith('.json'));
  if (documents.length === 0) {
    throw new Error('Backend build did not generate an OpenAPI document.');
  }

  return join(outputDirectory, documents[0]);
};

const generateClient = async (openApiDocument) => {
  console.log(`[api] generating Orval client from ${openApiDocument}`);

  const orvalPackage = require.resolve('orval/package.json');
  const orvalBin = resolve(dirname(orvalPackage), require(orvalPackage).bin.orval);

  await run(process.execPath, [orvalBin, '--config', 'orval.config.ts'], {
    cwd: frontendRoot,
    env: { ...process.env, OPENAPI_DOCUMENT: openApiDocument },
  });
};

const main = async () => {
  // An explicit document wins, so callers can generate from an already built
  // contract without repeating the backend build.
  const providedDocument = process.env.OPENAPI_DOCUMENT?.trim();
  if (providedDocument) {
    await generateClient(resolve(providedDocument));
    return;
  }

  const outputDirectory = await mkdtemp(join(tmpdir(), 'workslip-openapi-'));
  try {
    await generateClient(await buildOpenApiDocument(outputDirectory));
  } finally {
    await rm(outputDirectory, { recursive: true, force: true });
  }
};

try {
  await main();
} catch (error) {
  console.error(`[api] ${error.message}`);
  if (error.cause) {
    console.error('[api] the backend build requires the .NET SDK; see src/FE/README.md');
  }
  process.exitCode = 1;
}
