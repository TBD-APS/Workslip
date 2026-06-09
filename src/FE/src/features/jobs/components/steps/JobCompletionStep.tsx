import { CheckCircle2 } from 'lucide-react';
import type { JobForm } from '../../types';
import type { ReferenceDataResponse } from '../../../../api/generated/models';

type JobCompletionStepProps = {
  form: JobForm;
  referenceData: ReferenceDataResponse | null;
  isLoading: boolean;
  onClosureFlagsChange: (closureFlags: string[]) => void;
  worksheetCount: number;
};

export function JobCompletionStep({
  form,
  referenceData,
  isLoading,
  onClosureFlagsChange,
  worksheetCount,
}: JobCompletionStepProps) {
  const closureFlags = [...(referenceData?.closureFlags ?? [])]
    .sort((left, right) => Number(left.sortOrder) - Number(right.sortOrder));

  const toggleFlag = (flagLabel: string) => {
    const currentFlags = form.work.closureFlags || [];
    let nextFlags: string[];

    if (flagLabel === 'NotCompleted') {
      if (currentFlags.includes('NotCompleted')) {
        nextFlags = currentFlags.filter((f) => f !== 'NotCompleted');
      } else {
        nextFlags = [
          ...currentFlags.filter((f) => f !== 'Completed' && f !== 'ReadyForInvoice'),
          'NotCompleted',
        ];
      }
    } else if (flagLabel === 'Completed' || flagLabel === 'ReadyForInvoice') {
      if (currentFlags.includes(flagLabel)) {
        nextFlags = currentFlags.filter((f) => f !== flagLabel);
      } else {
        nextFlags = [...currentFlags.filter((f) => f !== 'NotCompleted'), flagLabel];
      }
    } else {
      if (currentFlags.includes(flagLabel)) {
        nextFlags = currentFlags.filter((f) => f !== flagLabel);
      } else {
        nextFlags = [...currentFlags, flagLabel];
      }
    }

    onClosureFlagsChange(nextFlags);
  };

  return (
    <section className="detail-section job-completion-section">
      <div className="section-header-row">
        <CheckCircle2 size={18} />
        <h3>Afslutning</h3>
      </div>

      <p className="subtitle" style={{ marginBottom: '1.5rem' }}>
        Sagen har {worksheetCount} {worksheetCount === 1 ? 'arbejdsseddel' : 'arbejdssedler'} og er klar til afslutning.
      </p>

      {isLoading && <p className="empty-state-text">Henter afslutningstyper...</p>}

      {!isLoading && (
        <div className="work-category-form">
          <div className="work-field-group">
            <span className="work-field-label">Vælg status for sagens afslutning</span>
            <div className="work-kind-list">
              {closureFlags.map((flag) => {
                const isSelected = (form.work.closureFlags || []).includes(flag.normalizedLabel);

                return (
                  <button
                    key={flag.id}
                    type="button"
                    className={`work-kind-option ${isSelected ? 'selected' : ''}`}
                    onClick={() => toggleFlag(flag.normalizedLabel)}
                    aria-pressed={isSelected}
                  >
                    <span>{flag.label}</span>
                  </button>
                );
              })}
            </div>
            {(!form.work.closureFlags || form.work.closureFlags.length === 0) && (
              <span className="form-help-error">Vælg mindst én afslutningsstatus.</span>
            )}
          </div>
        </div>
      )}
    </section>
  );
}
