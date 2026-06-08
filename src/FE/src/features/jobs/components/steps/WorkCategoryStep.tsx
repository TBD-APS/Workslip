import { useEffect, useRef } from 'react';
import { FileText } from 'lucide-react';
import type { JobForm, ReferenceData } from '../../types';

type WorkCategoryStepProps = {
  form: JobForm;
  referenceData: ReferenceData | null;
  isLoading: boolean;
  onCategoriesChange: (categoryIds: string[]) => void;
  onWorkKindChange: (workKind: string) => void;
  onCustomWorkKindChange: (customWorkKind: string) => void;
};

export function WorkCategoryStep({
  form,
  referenceData,
  isLoading,
  onCategoriesChange,
  onWorkKindChange,
  onCustomWorkKindChange,
}: WorkCategoryStepProps) {
  const customWorkKindRef = useRef<HTMLLabelElement | null>(null);
  const categories = [...(referenceData?.installationTypes ?? [])];
  const workKinds = [...(referenceData?.workKinds ?? [])]
    .sort((left, right) => Number(left.sortOrder) - Number(right.sortOrder));
  const selectedWorkKind = workKinds.find((kind) => kind.normalizedLabel === form.work.workKind);
  const requiresCustomWorkKind = selectedWorkKind?.requiresCustomWorkKind ?? false;

  useEffect(() => {
    if (requiresCustomWorkKind) {
      customWorkKindRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      setTimeout(() => customWorkKindRef.current?.querySelector<HTMLInputElement>('input')?.focus(), 350);
    }
  }, [requiresCustomWorkKind]);

  const toggleCategory = (categoryId: string) => {
    const nextCategoryIds = form.work.categoryIds.includes(categoryId)
      ? form.work.categoryIds.filter((id) => id !== categoryId)
      : [...form.work.categoryIds, categoryId];

    onCategoriesChange(nextCategoryIds);
  };

  return (
    <section className="detail-section work-category-section">
      <div className="section-header-row">
        <FileText size={18} />
        <h3>Anlægstyper</h3>
      </div>

      {isLoading && <p className="empty-state-text">Henter anlægstyper...</p>}

      {!isLoading && (
        <div className="work-category-form">
          <div className="work-field-group">
            <div className="category-choice-grid">
              {categories.map((category) => {
                const isSelected = form.work.categoryIds.includes(category.id);

                return (
                  <button
                    key={category.id}
                    type="button"
                    className={`choice-card selection-card ${isSelected ? 'selected' : ''}`}
                    onClick={() => toggleCategory(category.id)}
                    aria-pressed={isSelected}
                  >
                    <span>{category.name}</span>
                  </button>
                );
              })}
            </div>
            {form.work.categoryIds.length === 0 && (
              <span className="form-help-error">Vælg mindst én anlægstype.</span>
            )}
          </div>

          <div className="work-field-group">
            <span className="work-field-label">Opgavetype</span>
            <div className="work-kind-list">
              {workKinds.map((workKind) => {
                const isSelected = form.work.workKind === workKind.normalizedLabel;

                return (
                  <label key={workKind.normalizedLabel} className={`work-kind-option selection-row ${isSelected ? 'selected' : ''}`}>
                    <input
                      type="radio"
                      name="workKind"
                      value={workKind.normalizedLabel}
                      checked={isSelected}
                      onChange={(event) => onWorkKindChange(event.target.value)}
                    />
                    <span>{workKind.label}</span>
                  </label>
                );
              })}
            </div>
            {form.work.workKind.length === 0 && (
              <span className="form-help-error">Vælg en opgavetype.</span>
            )}
          </div>

          {requiresCustomWorkKind && (
            <label className="work-field-group" ref={customWorkKindRef}>
              <span className="work-field-label">Beskriv anden opgavetype</span>
              <input
                className="form-input"
                value={form.work.customWorkKind}
                onChange={(event) => onCustomWorkKindChange(event.target.value)}
                placeholder="Skriv hvilken opgavetype der udføres"
                required
              />
              {form.work.customWorkKind.trim().length === 0 && (
                <span className="form-help-error">Udfyld feltet for at fortsætte.</span>
              )}
            </label>
          )}
        </div>
      )}
    </section>
  );
}
