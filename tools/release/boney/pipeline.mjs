import { isDeepStrictEqual } from 'node:util';
import { validateProfile } from './profile.mjs';
import { verifyMergedRelease, createGitHubReleaseGate } from './github.mjs';

function requireArtifact(value, revision) {
  if (value?.revision !== revision || !/^[a-f0-9]{40}$/.test(revision ?? '')
      || !/^[a-f0-9]{64}$/.test(value?.artifact?.digest ?? '')
      || typeof value.artifact.reference !== 'string' || value.artifact.reference.length > 400
      || !value.artifact.reference.endsWith(`sha256:${value.artifact.digest}`)
      || /[\s?#]/.test(value.artifact.reference)) throw Error('ARTIFACT_IDENTITY_INVALID');
  if (value.artifact.reference.includes('://')) {
    const uri = new URL(value.artifact.reference);
    if (uri.username || uri.password || uri.protocol !== 'https:') throw Error('ARTIFACT_IDENTITY_INVALID');
  }
  return structuredClone({ revision, artifact: value.artifact });
}

function requireObservation(value, expected) {
  if (value?.healthy !== true || value.revision !== expected.revision || value.artifactDigest !== expected.artifact.digest) throw Error('OBSERVATION_IDENTITY_INVALID');
}

export async function authorizeRelease(profile, context, request) {
  const merge = await verifyMergedRelease(context, profile, request);
  if (!profile.ci) return merge;
  const evidence = await createGitHubReleaseGate({ ...profile, ...profile.ci }).resolveEvidence({ client: request, repository: context.repository, sha: context.sha, ciRunId: 0, waitSeconds: 0, pollSeconds: 5 });
  return { ...merge, ciRunId: evidence.runId };
}

export async function executeRelease({ profile: input, environment, context: inputContext, adapter, store, request }) {
  const profile = validateProfile(input), context = Object.freeze({ ...inputContext });
  const target = profile.environments[environment];
  if (!target || !adapter || !['prepare', 'deploy', 'observe'].every(method => typeof adapter[method] === 'function')
      || (target.migration !== 'none' && typeof adapter.migrate !== 'function')
      || (target.recovery === 'automatic' && typeof adapter.recover !== 'function')) throw Error('DELIVERY_ADAPTER_INVALID');
  const identity = { product: profile.product, environment, revision: context.sha };
  return store.withLock(identity, async session => {
    const existing = session.load();
    if (existing !== null) {
      if (existing?.schemaVersion !== 1 || existing.status !== 'released' || existing.repository !== profile.repository
          || existing.product !== identity.product || existing.environment !== identity.environment || existing.revision !== identity.revision
          || existing.githubEnvironment !== target.githubEnvironment) throw Error('RELEASE_RECONCILIATION_REQUIRED');
      try { requireArtifact(existing.candidate, identity.revision); }
      catch { throw Error('RELEASE_RECONCILIATION_REQUIRED'); }
      await authorizeRelease(profile, context, request);
      return existing;
    }
    let stage = 'checking', candidate, previous, mutationStarted = false, persistenceFailed = false;
    let state = { schemaVersion: 1, ...identity, repository: profile.repository, githubEnvironment: target.githubEnvironment, status: stage };
    const save = async (status, extra = {}) => {
      state = { ...state, ...extra, status };
      try {
        const receipt = await session.save(structuredClone(state));
        if (!Object.entries(state).every(([key, value]) => isDeepStrictEqual(receipt?.[key], value))) throw Error('RELEASE_RECEIPT_INVALID');
        return structuredClone(receipt);
      }
      catch { persistenceFailed = true; throw Error('RELEASE_RECONCILIATION_REQUIRED'); }
    };
    try {
      const admission = await authorizeRelease(profile, context, request);
      await save('admitted', { admission });
      stage = 'preparing'; await save(stage);
      const prepared = await adapter.prepare({ context, environment, target });
      candidate = requireArtifact(prepared, context.sha);
      if (target.migration === 'expand-contract' && prepared.migration?.compatibility !== 'expand-contract') throw Error('MIGRATION_POLICY_REFUSED');
      if (target.recovery === 'automatic') {
        previous = requireArtifact(prepared.previous, prepared.previous?.revision);
        if (previous.revision === candidate.revision) throw Error('RECOVERY_IDENTITY_INVALID');
      }
      await save('prepared', { candidate, ...(previous ? { previous } : {}) });
      const args = () => ({ context, environment, target, candidate: structuredClone(candidate), previous: previous && structuredClone(previous) });
      if (target.migration !== 'none') {
        await authorizeRelease(profile, context, request);
        stage = 'migrating'; await save(stage);
        const migration = await adapter.migrate(args());
        if (migration?.applied !== true || migration.revision !== context.sha) throw Error('MIGRATION_NOT_CONFIRMED');
        await save('migrated');
      }
      await authorizeRelease(profile, context, request);
      stage = 'deploying'; await save(stage); mutationStarted = true;
      const deployed = await adapter.deploy(args());
      if (deployed?.revision !== candidate.revision || deployed.artifactDigest !== candidate.artifact.digest) throw Error('DEPLOYMENT_IDENTITY_INVALID');
      stage = 'observing'; await save(stage);
      requireObservation(await adapter.observe(args()), candidate);
      return await save('released');
    } catch {
      // A store may have committed a receipt before losing its acknowledgement.
      // Its uncertain outcome must never trigger another provider mutation.
      if (persistenceFailed) throw Error('RELEASE_RECONCILIATION_REQUIRED');
      // Untrusted provider errors can contain credentials or response bodies.
      // Persist a bounded outcome, never arbitrary exception text or adapter output.
      if (mutationStarted && target.recovery === 'automatic' && previous) {
        await save('recovering', { failedStage: stage });
        try {
          requireObservation(await adapter.recover({ context, environment, target, candidate: structuredClone(candidate), previous: structuredClone(previous) }), previous);
          return await save('rolled-back', { recoveredRevision: previous.revision });
        } catch {
          if (persistenceFailed) throw Error('RELEASE_RECONCILIATION_REQUIRED');
          return await save('blocked', { reason: 'RECOVERY_NOT_CONFIRMED' });
        }
      }
      return await save('blocked', { failedStage: stage, reason: mutationStarted ? 'MANUAL_RECOVERY_REQUIRED' : 'RELEASE_STEP_FAILED' });
    }
  });
}
