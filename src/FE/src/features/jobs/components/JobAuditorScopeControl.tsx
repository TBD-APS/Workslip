import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { EyeOff, Loader2, ShieldCheck } from 'lucide-react';
import { notify } from '../../../lib/toast';
import { getJobAuditorScope, setJobAuditorScope } from '../api/auditorScopeApi';
import './jobAuditorScope.css';

const auditorScopeKey = (jobId: string) => ['job-auditor-scope', jobId] as const;

export function JobAuditorScopeControl({ jobId }: { jobId: string }) {
  const queryClient = useQueryClient();
  const [editingReason, setEditingReason] = useState(false);
  const [reason, setReason] = useState('');

  const scopeQuery = useQuery({
    queryKey: auditorScopeKey(jobId),
    queryFn: () => getJobAuditorScope(jobId),
  });

  const mutation = useMutation({
    mutationFn: (next: { isInAuditorScope: boolean; reason?: string | null }) =>
      setJobAuditorScope(jobId, next),
    onSuccess: (scope) => {
      queryClient.setQueryData(auditorScopeKey(jobId), scope);
      setEditingReason(false);
      setReason('');
      notify.success(
        scope.isInAuditorScope
          ? 'Sagen vises igen i auditørens arbejdsflade'
          : 'Sagen er nu intern og vises ikke til auditør',
      );
    },
    onError: () => {
      notify.error('Auditøradgangen kunne ikke opdateres');
    },
  });

  if (scopeQuery.isLoading) {
    return (
      <section className="auditor-scope-card" aria-label="Auditøradgang">
        <Loader2 className="animate-spin" size={18} aria-hidden="true" />
        <span>Henter auditøradgang...</span>
      </section>
    );
  }

  if (scopeQuery.isError || !scopeQuery.data) {
    return (
      <section className="auditor-scope-card auditor-scope-card--error" aria-label="Auditøradgang">
        <div>
          <strong>Auditøradgang kunne ikke hentes</strong>
          <p>Prøv igen før du ændrer sagens audit-scope.</p>
        </div>
        <button className="btn btn-secondary" type="button" onClick={() => scopeQuery.refetch()}>
          Prøv igen
        </button>
      </section>
    );
  }

  const scope = scopeQuery.data;
  const normalizedReason = reason.trim();
  const canHide = normalizedReason.length >= 3 && !mutation.isPending;

  return (
    <section
      className={`auditor-scope-card ${scope.isInAuditorScope ? '' : 'auditor-scope-card--internal'}`}
      aria-labelledby="auditor-scope-title"
    >
      <div className="auditor-scope-card__icon" aria-hidden="true">
        {scope.isInAuditorScope ? <ShieldCheck size={20} /> : <EyeOff size={20} />}
      </div>
      <div className="auditor-scope-card__body">
        <div className="auditor-scope-card__heading">
          <div>
            <span className="auditor-scope-card__eyebrow">Auditøradgang</span>
            <h2 id="auditor-scope-title">
              {scope.isInAuditorScope ? 'Med i auditørvisningen' : 'Intern sag'}
            </h2>
          </div>
          {!editingReason && (
            scope.isInAuditorScope ? (
              <button
                className="btn btn-secondary"
                type="button"
                onClick={() => setEditingReason(true)}
                disabled={mutation.isPending}
              >
                Gør intern
              </button>
            ) : (
              <button
                className="btn btn-secondary"
                type="button"
                onClick={() => mutation.mutate({ isInAuditorScope: true, reason: null })}
                disabled={mutation.isPending}
              >
                {mutation.isPending ? 'Gemmer...' : 'Vis for auditør'}
              </button>
            )
          )}
        </div>

        <p className="auditor-scope-card__description">
          {scope.isInAuditorScope
            ? 'Sagen kan vises til auditør, når den også matcher auditørens faglige scope.'
            : 'Sagen vises ikke i auditørens lister, direkte opslag, historik, PDF eller jobbilleder.'}
        </p>

        {!scope.isInAuditorScope && scope.reason && (
          <p className="auditor-scope-card__reason">
            <strong>Begrundelse:</strong> {scope.reason}
          </p>
        )}

        {editingReason && (
          <div className="auditor-scope-card__editor">
            <label htmlFor={`auditor-scope-reason-${jobId}`}>Hvorfor skal sagen være intern?</label>
            <textarea
              id={`auditor-scope-reason-${jobId}`}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              rows={3}
              autoFocus
              placeholder="Fx intern opgave uden for kundens aftalte audit-scope"
            />
            <div className="auditor-scope-card__editor-footer">
              <span>{reason.length}/500</span>
              <div className="auditor-scope-card__actions">
                <button
                  className="btn btn-secondary"
                  type="button"
                  onClick={() => {
                    setEditingReason(false);
                    setReason('');
                  }}
                  disabled={mutation.isPending}
                >
                  Annuller
                </button>
                <button
                  className="btn btn-primary"
                  type="button"
                  onClick={() => mutation.mutate({ isInAuditorScope: false, reason: normalizedReason })}
                  disabled={!canHide}
                >
                  {mutation.isPending ? 'Gemmer...' : 'Gør sagen intern'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </section>
  );
}
