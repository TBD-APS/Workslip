import { useEffect, useState } from 'react';
import { EyeOff, ShieldCheck } from 'lucide-react';
import type { JobAuditorScopeDraft } from '../api/auditorScopeApi';
import './jobAuditorScope.css';

type JobAuditorScopeControlProps = {
  value: JobAuditorScopeDraft;
  onChange: (next: JobAuditorScopeDraft) => void;
  disabled?: boolean;
};

export function JobAuditorScopeControl({ value, onChange, disabled = false }: JobAuditorScopeControlProps) {
  const [editingReason, setEditingReason] = useState(false);
  const [draftReason, setDraftReason] = useState(value.reason);

  useEffect(() => {
    if (!editingReason) setDraftReason(value.reason);
  }, [editingReason, value.reason]);

  const normalizedReason = draftReason.trim();
  const canMakeInternal = normalizedReason.length >= 3 && !disabled;

  const startInternalEdit = () => {
    setDraftReason(value.reason);
    setEditingReason(true);
  };

  const cancelInternalEdit = () => {
    setDraftReason(value.reason);
    setEditingReason(false);
  };

  const makeInternal = () => {
    if (!canMakeInternal) return;
    onChange({ isInAuditorScope: false, reason: normalizedReason });
    setEditingReason(false);
  };

  const makeVisible = () => {
    onChange({ isInAuditorScope: true, reason: '' });
    setDraftReason('');
    setEditingReason(false);
  };

  return (
    <section
      className={`auditor-scope-card auditor-scope-card--create ${value.isInAuditorScope ? '' : 'auditor-scope-card--internal'}`}
      aria-labelledby="auditor-scope-title"
    >
      <div className="auditor-scope-card__icon" aria-hidden="true">
        {value.isInAuditorScope ? <ShieldCheck size={20} /> : <EyeOff size={20} />}
      </div>
      <div className="auditor-scope-card__body">
        <div className="auditor-scope-card__heading">
          <div>
            <span className="auditor-scope-card__eyebrow">Auditøradgang</span>
            <h2 id="auditor-scope-title">
              {value.isInAuditorScope ? 'Med i auditørvisningen' : 'Intern sag'}
            </h2>
          </div>
          {!editingReason && (
            value.isInAuditorScope ? (
              <button
                className="btn btn-secondary"
                type="button"
                onClick={startInternalEdit}
                disabled={disabled}
              >
                Gør intern
              </button>
            ) : (
              <div className="auditor-scope-card__actions auditor-scope-card__actions--heading">
                <button
                  className="btn btn-secondary"
                  type="button"
                  onClick={startInternalEdit}
                  disabled={disabled}
                >
                  Rediger begrundelse
                </button>
                <button
                  className="btn btn-secondary"
                  type="button"
                  onClick={makeVisible}
                  disabled={disabled}
                >
                  Vis for auditør
                </button>
              </div>
            )
          )}
        </div>

        <p className="auditor-scope-card__description">
          {value.isInAuditorScope
            ? 'Sagen kan vises til auditør, når den også matcher auditørens faglige scope.'
            : 'Sagen oprettes som intern og holdes ude af auditørens arbejdsflade.'}
        </p>

        {!value.isInAuditorScope && value.reason && !editingReason && (
          <p className="auditor-scope-card__reason">
            <strong>Begrundelse:</strong> {value.reason}
          </p>
        )}

        {editingReason && (
          <div className="auditor-scope-card__editor">
            <label htmlFor="auditor-scope-create-reason">Hvorfor skal sagen være intern?</label>
            <textarea
              id="auditor-scope-create-reason"
              value={draftReason}
              onChange={(event) => setDraftReason(event.target.value)}
              maxLength={500}
              rows={3}
              autoFocus
              disabled={disabled}
              placeholder="Fx intern opgave uden for kundens aftalte audit-scope"
            />
            <div className="auditor-scope-card__editor-footer">
              <span>{draftReason.length}/500</span>
              <div className="auditor-scope-card__actions">
                <button
                  className="btn btn-secondary"
                  type="button"
                  onClick={cancelInternalEdit}
                  disabled={disabled}
                >
                  Annuller
                </button>
                <button
                  className="btn btn-primary"
                  type="button"
                  onClick={makeInternal}
                  disabled={!canMakeInternal}
                >
                  Gør sagen intern
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </section>
  );
}
