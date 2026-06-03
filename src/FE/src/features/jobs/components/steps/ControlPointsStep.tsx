import { useState } from 'react';
import { ChevronRight, ClipboardList, CheckCircle2 } from 'lucide-react';
import { Checkbox } from '../../../../components/forms/Checkbox';
import type { JobForm, ReferenceData } from '../../types';

type ControlPointsStepProps = {
  form: JobForm;
  referenceData: ReferenceData | null;
  onToggleControlPoint: (cpId: string) => void;
  onToggleCategoryIrrelevant: (categoryId: string) => void;
};

export function validateControlPoints(
  form: JobForm,
  referenceData: ReferenceData | null
): { valid: boolean; error?: string } {
  const selectedTypes = (referenceData?.installationTypes ?? [])
    .filter((t) => form.work.categoryIds.includes(t.id));

  for (const instType of selectedTypes) {
    let hasAnyControlPoint = false;

    for (const cat of instType.categories) {
      const compositeId = `${instType.id}-${cat.id}`;
      const isIrrelevant = form.work.irrelevantCategoryIds.includes(compositeId);

      if (!isIrrelevant) {
        const hasSelectedControlPoint = (cat.controlPoints ?? []).some(
          (cp) => form.work.controlPointSelections[cp.id]
        );

        if (hasSelectedControlPoint) {
          hasAnyControlPoint = true;
        } else {
          return {
            valid: false,
            error: `Mindst et kontrolpunkt skal vælges for "${capitalizeFirstLetter(cat.name)}"`,
          };
        }
      }
    }

    // Check if at least one category in this installation type has a control point selected
    if (!hasAnyControlPoint) {
      return {
        valid: false,
        error: `Mindst én kategori i "${instType.name}" skal have et kontrolpunkt valgt (kan ikke markere alle som "ikke relevant")`,
      };
    }
  }

  return { valid: true };
}

function bySortOrder(a: { sortOrder: string | number }, b: { sortOrder: string | number }) {
  return Number(a.sortOrder) - Number(b.sortOrder);
}

function capitalizeFirstLetter(str: string): string {
  if (!str) return str;
  return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase();
}

export function ControlPointsStep({
  form,
  referenceData,
  onToggleControlPoint,
  onToggleCategoryIrrelevant,
}: ControlPointsStepProps) {
  const [validationError, setValidationError] = useState<string | null>(null);

  // Show loading state if reference data is not ready yet
  if (!referenceData) {
    return (
      <section className="detail-section">
        <div className="section-header-row">
          <ClipboardList size={18} />
          <h3>Kontrolpunkter</h3>
        </div>
        <p className="empty-state-text">Henter kontrolpunkter...</p>
      </section>
    );
  }

  const selectedTypes = (referenceData.installationTypes ?? [])
    .filter((t) => form.work.categoryIds.includes(t.id));

  if (selectedTypes.length === 0) {
    return (
      <section className="detail-section">
        <div className="section-header-row">
          <ClipboardList size={18} />
          <h3>Kontrolpunkter</h3>
        </div>
        <p className="empty-state-text">Vælg mindst én kategori for at se kontrolpunkter.</p>
      </section>
    );
  }

  return (
    <section className="detail-section">
      <div className="section-header-row">
        <ClipboardList size={18} />
        <h3>Kontrolpunkter</h3>
      </div>

      {validationError && (
        <div className="validation-error">
          <span>{validationError}</span>
        </div>
      )}

      {selectedTypes.sort(bySortOrder).map((instType) => (
        <InstallationTypeCard
          key={instType.id}
          name={instType.name}
          defaultExpanded
        >
          {instType.categories.sort(bySortOrder).map((cat) => {
            const compositeId = `${instType.id}-${cat.id}`;
            const isIrrelevant = form.work.irrelevantCategoryIds.includes(compositeId);

            return (
              <div key={cat.id} className={`control-point-category-group${isIrrelevant ? ' irrelevant' : ''}`}>
                <div className="control-point-category-header-row">
                  <div className="control-point-category-header">
                    <span className="control-point-category-label">{capitalizeFirstLetter(cat.name)}</span>
                  </div>

                  <button
                    className={`multi-select-option checkbox-right${isIrrelevant ? ' selected' : ''}`}
                    type="button"
                    onClick={() => onToggleCategoryIrrelevant(`${instType.id}-${cat.id}`)}
                    aria-label={`${isIrrelevant ? 'Marker' : 'Umarker'} ${cat.name} som ${isIrrelevant ? 'relevant' : 'ikke relevant'}`}
                    title={isIrrelevant ? 'Marker som relevant' : 'Marker som ikke relevant'}
                  >
                    <span className="multi-select-checkbox" aria-hidden="true">
                      {isIrrelevant && <CheckCircle2 size={14} />}
                    </span>
                    <span className="multi-select-option-text">
                      <span>Irrelevant</span>
                    </span>
                  </button>
                </div>

                {!isIrrelevant && (
                  <div className="control-points-list">
                    {(cat.controlPoints ?? []).sort(bySortOrder).map((cp) => (
                      <Checkbox
                        key={cp.id}
                        checked={form.work.controlPointSelections[cp.id] ?? false}
                        label={cp.name}
                        description={cp.description ?? undefined}
                        onChange={() => onToggleControlPoint(cp.id)}
                        alignRight
                      />
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </InstallationTypeCard>
      ))}
    </section>
  );
}

function InstallationTypeCard({
  name,
  defaultExpanded,
  children,
}: {
  name: string;
  defaultExpanded?: boolean;
  children: React.ReactNode;
}) {
  const [isExpanded, setIsExpanded] = useState(defaultExpanded ?? true);

  return (
    <div className={`control-point-type${isExpanded ? '' : ' collapsed'}`}>
      <button
        className="control-point-type-header"
        type="button"
        onClick={() => setIsExpanded((open) => !open)}
        aria-expanded={isExpanded}
      >
        <ChevronRight size={18} className={`control-point-chevron${isExpanded ? ' open' : ''}`} />
        <span className="control-point-type-name">{name}</span>
      </button>

      {isExpanded && <div className="control-point-type-body">{children}</div>}
    </div>
  );
}
