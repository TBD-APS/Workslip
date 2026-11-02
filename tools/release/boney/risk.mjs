// Policy data is supplied by the product; the engine has no filesystem or CLI defaults.
export const TIER_ORDER = Object.freeze(['low', 'medium', 'high']);

function record(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function nonempty(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function validatePolicy(policy) {
  if (!record(policy) || !record(policy.operations) || !record(policy.tiers) || !Array.isArray(policy.pathRules)) {
    throw Error('An explicit risk policy with operations, tiers and path rules is required.');
  }
  rank(policy.unmatchedPathTier);
  rank(policy.unknownBaselineTier);
  for (const tier of TIER_ORDER) {
    const spec = policy.tiers[tier];
    if (!Object.hasOwn(policy.tiers, tier) || !record(spec) || !nonempty(spec.environment)
        || (spec.selfHealEnvironment != null && !nonempty(spec.selfHealEnvironment))
        || !Number.isSafeInteger(spec.waitTimerMinutes) || spec.waitTimerMinutes < 0
        || typeof spec.confirmationSuffix !== 'string'
        || (spec.requiresOwnerApproval !== undefined && typeof spec.requiresOwnerApproval !== 'boolean')) {
      throw Error(`Complete risk policy data is required for tier '${tier}'.`);
    }
  }
  for (const [name, operation] of Object.entries(policy.operations)) {
    if (!record(operation) || (operation.note !== undefined && typeof operation.note !== 'string')) {
      throw Error(`Invalid risk policy operation '${name}'.`);
    }
    if (operation.floor !== undefined) rank(operation.floor);
  }
  return policy.pathRules.map(rule => {
    if (!record(rule) || !nonempty(rule.reason) || !Array.isArray(rule.globs) || !rule.globs.length) {
      throw Error('Risk policy path rules require a tier, reason and non-empty globs array.');
    }
    rank(rule.tier);
    return { ...rule, matchers: rule.globs.map(globToRegExp) };
  });
}

function validateChangedFiles(changedFiles) {
  if (changedFiles === null) return;
  if (!Array.isArray(changedFiles) || changedFiles.some(file => typeof file !== 'string' || !file.length
      || file.startsWith('/') || /[\u0000-\u001f\u007f\\]/.test(file)
      || file.split('/').some(segment => !segment || segment === '.' || segment === '..'))) {
    throw Error('Changed files must be complete, repository-relative paths, or null for an unknown baseline.');
  }
}

function rank(tier) {
  const index = TIER_ORDER.indexOf(tier);
  if (index < 0) throw new Error(`Unknown tier '${tier}'.`);
  return index;
}

function higher(a, b) {
  return rank(a) >= rank(b) ? a : b;
}

// Minimal glob: ** matches across separators, * within one segment. Enough for a policy
// file people read; anything cleverer would need a dependency the release tooling avoids.
export function globToRegExp(glob) {
  if (typeof glob !== 'string' || !glob.length) throw Error('A non-empty string glob is required.');
  let pattern = '';
  for (let i = 0; i < glob.length; i++) {
    const char = glob[i];
    if (char === '*') {
      if (glob[i + 1] === '*') {
        i++;
        if (glob[i + 1] === '/') { i++; pattern += '(?:.*/)?'; } else { pattern += '.*'; }
      } else {
        pattern += '[^/]*';
      }
    } else if ('.+?^${}()|[]\\'.includes(char)) {
      pattern += `\\${char}`;
    } else {
      pattern += char;
    }
  }
  return new RegExp(`^${pattern}$`);
}

/**
 * @param {object} input
 * @param {string} input.operation           workflow operation, e.g. deploy, reconcile
 * @param {string[]|null} input.changedFiles  paths changed since the last successful run; null = baseline unknown
 * @param {string} [input.override]           'auto' or a tier name; may only raise
 * @param {object} input.policy              product-owned path, operation and environment policy
 */
export function classify({ operation, changedFiles, override = 'auto', policy }) {
  const rules = validatePolicy(policy);
  validateChangedFiles(changedFiles);
  const reasons = [];
  const op = Object.hasOwn(policy.operations, operation) ? policy.operations[operation] : null;
  let tier = 'low';

  if (!op) {
    tier = 'high';
    reasons.push(`operation '${operation}' is not in the policy; unknown operations are high risk`);
  } else if (op.floor) {
    tier = higher(tier, op.floor);
    if (op.floor !== 'low') reasons.push(`operation '${operation}' has a floor of ${op.floor}${op.note ? ` (${op.note})` : ''}`);
  }

  if (changedFiles === null) {
    tier = higher(tier, policy.unknownBaselineTier);
    reasons.push(`no successful previous run of this operation was found, so the change cannot be bounded; treated as ${policy.unknownBaselineTier}`);
  } else {
    const hits = new Map();
    const unmatched = [];
    for (const file of changedFiles) {
      // First matching rule wins per file; rules are ordered highest-risk first.
      const rule = rules.find(candidate => candidate.matchers.some(matcher => matcher.test(file)));
      if (!rule) { unmatched.push(file); continue; }
      const entry = hits.get(rule) ?? { tier: rule.tier, files: [] };
      entry.files.push(file);
      hits.set(rule, entry);
    }
    for (const [rule, entry] of hits) {
      tier = higher(tier, entry.tier);
      reasons.push(`${entry.tier}: ${rule.reason} — ${entry.files.length} file(s), e.g. ${entry.files[0]}`);
    }
    if (unmatched.length > 0) {
      tier = higher(tier, policy.unmatchedPathTier);
      reasons.push(`${policy.unmatchedPathTier}: ${unmatched.length} file(s) matched no rule, e.g. ${unmatched[0]}`);
    }
    if (changedFiles.length === 0) reasons.push('no files changed since the last successful run of this operation');
  }

  if (override && override !== 'auto') {
    rank(override);
    if (rank(override) > rank(tier)) {
      reasons.push(`raised from ${tier} to ${override} by operator override`);
      tier = override;
    } else if (rank(override) < rank(tier)) {
      reasons.push(`operator override '${override}' ignored: the computed tier ${tier} cannot be lowered`);
    }
  }

  const spec = policy.tiers[tier];
  return {
    tier,
    environment: spec.environment,
    selfHealEnvironment: spec.selfHealEnvironment ?? null,
    waitTimerMinutes: spec.waitTimerMinutes,
    confirmationSuffix: spec.confirmationSuffix,
    requiresOwnerApproval: spec.requiresOwnerApproval ?? true,
    reasons,
  };
}

/**
 * The manual path always uses the tier environment, with the reviewer its policy names.
 * Self-heal may use the tier's self-heal environment, and only when that tier has one.
 */
export function admitEnvironment(result, admittedBy = 'owner') {
  if (!record(result) || !nonempty(result.environment) || typeof result.requiresOwnerApproval !== 'boolean'
      || (result.selfHealEnvironment != null && !nonempty(result.selfHealEnvironment))) {
    throw Error('A complete classified environment result is required.');
  }
  if (admittedBy === 'owner') {
    return { environment: result.environment, requiresOwnerApproval: result.requiresOwnerApproval };
  }
  if (admittedBy === 'self-heal' && result.selfHealEnvironment) {
    return { environment: result.selfHealEnvironment, requiresOwnerApproval: false };
  }
  throw new Error(`self-heal cannot start a ${result.tier} operation`);
}
