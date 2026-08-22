import assert from 'node:assert/strict';
import test from 'node:test';
import {
  loadArchitectureOwners,
  resolveArchitectureOwner,
  validateArchitectureOwners,
} from './architecture-owners.mjs';

test('registry is structurally valid and all registered paths exist', async () => {
  const registry = await loadArchitectureOwners();
  assert.deepEqual(await validateArchitectureOwners(registry), []);
});

test('date resolves to the shared frontend presentation owner', async () => {
  const registry = await loadArchitectureOwners();
  const owner = resolveArchitectureOwner(registry, 'date');
  assert.equal(owner?.key, 'frontend.presentation');
  assert.equal(owner?.path, 'src/FE/src/lib/presentation');
  assert.equal(owner?.instructions, 'src/FE/src/lib/presentation/AGENTS.md');
});

test('model routing resolves to the Sassy agent runtime owner', async () => {
  const registry = await loadArchitectureOwners();
  const owner = resolveArchitectureOwner(registry, 'model-routing');
  assert.equal(owner?.key, 'agent.runtime');
});

test('unknown intents fail closed instead of guessing', async () => {
  const registry = await loadArchitectureOwners();
  assert.equal(resolveArchitectureOwner(registry, 'totally-unknown-intent'), null);
});
