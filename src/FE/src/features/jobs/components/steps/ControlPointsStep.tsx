import { useState } from 'react';
import { ChevronRight, ClipboardList } from 'lucide-react';
import { Checkbox } from '../../../../components/forms/Checkbox';
import type { JobForm } from '../../types';
import type { ReferenceDataResponse } from '../../../../api/generated/models';
import './ControlPointsStep.css';

type ControlPointsStepProps = {
  form: JobForm;
  referenceData: ReferenceDataResponse;
  onToggleControlPoint: (cpId: string) => void;
  onToggleCategoryIrrelevant: (typeId: string, categoryId: string) => void;
};

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
  const [validationError] = useState<string | null>(null);

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

  const selectedTypes = referenceData.installationTypes.filter((t) => form.work.categoryIds.includes(t.id));

  if (selectedTypes.length === 0) {
    return (
      <section className="detail-section">
        <div className="section-header-row">
          <ClipboardList size={18} />
          <h3>Kontrolpunkter</h3>
        </div>
        <p className="empty-state-text">Vælg mindst én anlægstype for at se kontrolpunkter.</p>
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
                <div className="control-point-category-header">
                  <span className="control-point-category-label">{capitalizeFirstLetter(cat.name)}</span>
                </div>

                <div className="control-points-list">
                  <button
                    className={`multi-select-option selection-row control-point-irrelevant-row${isIrrelevant ? ' selected' : ''}`}
                    type="button"
                    onClick={() => onToggleCategoryIrrelevant(instType.id, cat.id)}
                    aria-label={`${isIrrelevant ? 'Marker' : 'Umarker'} ${cat.name} som ${isIrrelevant ? 'relevant' : 'ikke relevant'}`}
                    title={isIrrelevant ? 'Marker som relevant' : 'Marker som ikke relevant'}
                    aria-pressed={isIrrelevant}
                  >
                    <span className="multi-select-option-text">
                      <span>Irrelevant</span>
                    </span>
                  </button>

                  {!isIrrelevant && (cat.controlPoints ?? []).sort(bySortOrder).map((cp) => (
                    <Checkbox
                      key={cp.id}
                      checked={form.work.controlPointSelections[cp.id] ?? false}
                      label={cp.name}
                      onChange={() => onToggleControlPoint(cp.id)}
                      alignRight
                    />
                  ))}
                </div>
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
