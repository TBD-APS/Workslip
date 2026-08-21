import { access, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const REGISTRY_PATH = path.join(REPO_ROOT, 'Docs', 'architecture', 'owners.json');

export async function loadArchitectureOwners() {
  const raw = await readFile(REGISTRY_PATH, 'utf8');
  return JSON.parse(raw);
}

export function resolveArchitectureOwner(registry, intent) {
  const normalized = String(intent ?? '').trim().toLowerCase();
  if (!normalized) return null;

  const matches = Object.entries(registry.owners ?? {}).filter(([, owner]) =>
    (owner.intents ?? []).some(candidate => String(candidate).toLowerCase() === normalized),
  );

  if (matches.length === 0) return null;
  if (matches.length > 1) {
    throw new Error(`Architecture intent '${normalized}' is ambiguous across: ${matches.map(([key]) => key).join(', ')}`);
  }

  const [key, owner] = matches[0];
  return { key, ...owner };
}

export async function validateArchitectureOwners(registry) {
  const errors = [];

  if (registry.version !== 1) errors.push(`Unsupported owners.json version: ${registry.version}`);
  if (!registry.owners || typeof registry.owners !== 'object' || Array.isArray(registry.owners)) {
    errors.push('owners.json must contain an owners object.');
    return errors;
  }

  const seenIntents = new Map();
  for (const [key, owner] of Object.entries(registry.owners)) {
    if (!owner.path) errors.push(`${key}: missing path`);
    if (!owner.instructions) errors.push(`${key}: missing instructions`);
    if (!Array.isArray(owner.intents) || owner.intents.length === 0) errors.push(`${key}: intents must be a non-empty array`);
    if (!owner.summary) errors.push(`${key}: missing summary`);

    for (const relativePath of [owner.path, owner.instructions]) {
      if (!relativePath) continue;
      try {
        await access(path.join(REPO_ROOT, relativePath));
      } catch {
        errors.push(`${key}: registered path does not exist: ${relativePath}`);
      }
    }

    for (const rawIntent of owner.intents ?? []) {
      const intent = String(rawIntent).trim().toLowerCase();
      if (!intent) {
        errors.push(`${key}: contains an empty intent`);
        continue;
      }
      const previous = seenIntents.get(intent);
      if (previous && previous !== key) errors.push(`intent '${intent}' is owned by both ${previous} and ${key}`);
      else seenIntents.set(intent, key);
    }
  }

  for (const instruction of registry.bootstrap?.instructions ?? []) {
    try {
      await access(path.join(REPO_ROOT, instruction));
    } catch {
      errors.push(`bootstrap instruction does not exist: ${instruction}`);
    }
  }

  return errors;
}
