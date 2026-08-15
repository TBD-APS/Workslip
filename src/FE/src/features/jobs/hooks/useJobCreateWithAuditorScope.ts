import { useCallback, useState } from 'react';
import { notify } from '../../../lib/toast';
import type { JobAuditorScopeDraft } from '../api/auditorScopeApi';
import { setJobAuditorScope } from '../api/auditorScopeApi';
import type { JobForm } from '../types';
import { useJobCreate } from './useJobCreate';

const DEFAULT_AUDITOR_SCOPE: JobAuditorScopeDraft = {
  isInAuditorScope: true,
  reason: '',
};

/**
 * Keeps the Admin-only auditor decision on the creation surface instead of the
 * six-step job flow. The existing backend deliberately owns auditor scope in a
 * separate Admin mutation, so creation first produces a Draft with no work /
 * installation selection and then applies the scope before this hook lets the
 * user leave the create flow. A Draft without a matching installation type
 * cannot pass the server-side auditor discipline scope.
 */
export function useJobCreateWithAuditorScope(
  onCreated: (jobIds: string[]) => void,
  initialForm?: JobForm,
) {
  const [auditorScope, setAuditorScope] = useState<JobAuditorScopeDraft>(DEFAULT_AUDITOR_SCOPE);
  const [pendingAuditorScopeJobIds, setPendingAuditorScopeJobIds] = useState<string[]>([]);
  const [allCreatedJobIds, setAllCreatedJobIds] = useState<string[]>([]);
  const [isSavingAuditorScope, setIsSavingAuditorScope] = useState(false);
  const [auditorScopeError, setAuditorScopeError] = useState(false);

  const applyAuditorScope = useCallback(async (
    jobIds: string[],
    completeCreatedJobIds: string[],
  ) => {
    if (auditorScope.isInAuditorScope) {
      setPendingAuditorScopeJobIds([]);
      setAllCreatedJobIds([]);
      setAuditorScopeError(false);
      onCreated(completeCreatedJobIds);
      return;
    }

    setIsSavingAuditorScope(true);
    setAuditorScopeError(false);

    const reason = auditorScope.reason.trim();
    const results = await Promise.allSettled(
      jobIds.map(async (jobId) => {
        await setJobAuditorScope(jobId, {
          isInAuditorScope: false,
          reason,
        });
        return jobId;
      }),
    );

    const failedJobIds = results.flatMap((result, index) =>
      result.status === 'rejected' ? [jobIds[index]] : [],
    );

    setIsSavingAuditorScope(false);

    if (failedJobIds.length > 0) {
      setPendingAuditorScopeJobIds(failedJobIds);
      setAllCreatedJobIds(completeCreatedJobIds);
      setAuditorScopeError(true);
      notify.error(
        'Sagen er oprettet, men auditøradgangen kunne ikke gemmes. Prøv igen herfra.',
        { id: 'job-create-auditor-scope-error' },
      );
      return;
    }

    setPendingAuditorScopeJobIds([]);
    setAllCreatedJobIds([]);
    setAuditorScopeError(false);
    onCreated(completeCreatedJobIds);
  }, [auditorScope.isInAuditorScope, auditorScope.reason, onCreated]);

  const handleBaseCreated = useCallback((jobIds: string[]) => {
    setAllCreatedJobIds(jobIds);
    void applyAuditorScope(jobIds, jobIds);
  }, [applyAuditorScope]);

  const create = useJobCreate(handleBaseCreated, initialForm);

  const retryAuditorScope = useCallback(() => {
    if (pendingAuditorScopeJobIds.length === 0 || allCreatedJobIds.length === 0) return;
    void applyAuditorScope(pendingAuditorScopeJobIds, allCreatedJobIds).then(() => {
      notify.success('Auditøradgangen er gemt', { id: 'job-create-auditor-scope-retry-success' });
    });
  }, [allCreatedJobIds, applyAuditorScope, pendingAuditorScopeJobIds]);

  const save = useCallback(() => {
    if (pendingAuditorScopeJobIds.length > 0) {
      retryAuditorScope();
      return;
    }
    create.save();
  }, [create, pendingAuditorScopeJobIds.length, retryAuditorScope]);

  const reset = useCallback((
    preserve?: { customerId?: string | null; customerSnapshot?: Parameters<typeof create.reset>[0] extends infer P ? P extends { customerSnapshot?: infer S } ? S : never : never },
  ) => {
    create.reset(preserve as Parameters<typeof create.reset>[0]);
    setAuditorScope(DEFAULT_AUDITOR_SCOPE);
    setPendingAuditorScopeJobIds([]);
    setAllCreatedJobIds([]);
    setIsSavingAuditorScope(false);
    setAuditorScopeError(false);
  }, [create]);

  return {
    ...create,
    save,
    reset,
    auditorScope,
    updateAuditorScope: setAuditorScope,
    auditorScopeError,
    retryAuditorScope,
    hasPendingAuditorScope: pendingAuditorScopeJobIds.length > 0,
    isSaving: create.isSaving || isSavingAuditorScope,
    canSave: create.canSave && pendingAuditorScopeJobIds.length === 0,
  };
}
