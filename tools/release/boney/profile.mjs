import { readFile } from 'node:fs/promises';

const slug = /^[a-z][a-z0-9-]{0,63}$/;
const repository = /^[A-Za-z0-9_-][A-Za-z0-9_.-]*\/[A-Za-z0-9_-][A-Za-z0-9_.-]*$/;
const branch = /^[A-Za-z0-9][A-Za-z0-9._/-]{0,100}$/;
const fail = () => { throw Error('DELIVERY_PROFILE_INVALID'); };
function object(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || Object.keys(value).some(key => !keys.includes(key))) fail();
}
function list(value, predicate) {
  if (!Array.isArray(value) || !value.length || value.length > 100 || new Set(value).size !== value.length || !value.every(predicate)) fail();
}

export function validateProfile(profile) {
  object(profile, ['schemaVersion', 'product', 'repository', 'branch', 'mergerIds', 'events', 'ci', 'environments']);
  if (profile.schemaVersion !== 1 || !slug.test(profile.product ?? '') || !repository.test(profile.repository ?? '')
      || !branch.test(profile.branch ?? '') || profile.branch.includes('..') || profile.branch.endsWith('/')) fail();
  list(profile.mergerIds, id => Number.isSafeInteger(id) && id > 0);
  list(profile.events, event => ['push', 'workflow_dispatch'].includes(event));
  if (profile.ci !== undefined) {
    object(profile.ci, ['workflow', 'requiredJobs']);
    if (!/^[A-Za-z0-9_-][A-Za-z0-9_.-]*\.ya?ml$/.test(profile.ci.workflow ?? '')) fail();
    list(profile.ci.requiredJobs, name => typeof name === 'string' && name.length > 0 && name.length < 150 && !/[\r\n]/.test(name));
  }
  if (!profile.environments || typeof profile.environments !== 'object' || Array.isArray(profile.environments) || !Object.keys(profile.environments).length) fail();
  for (const [name, environment] of Object.entries(profile.environments)) {
    object(environment, ['githubEnvironment', 'migration', 'recovery']);
    if (!slug.test(name) || !slug.test(environment.githubEnvironment ?? '')
        || !['none', 'expand-contract'].includes(environment.migration)
        || !['manual', 'automatic'].includes(environment.recovery)) fail();
  }
  // A caller cannot change policy after admission while an adapter is executing.
  const result = structuredClone(profile);
  function freeze(value) { if (value && typeof value === 'object') { for (const item of Object.values(value)) freeze(item); Object.freeze(value); } return value; }
  return freeze(result);
}

export async function loadProfile(path) { return validateProfile(JSON.parse(await readFile(path, 'utf8'))); }
