import { CheckCircle2, FileText } from 'lucide-react';
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
  const categories = [...(referenceData?.installationTypes ?? [])];
  const workKinds = [...(referenceData?.workKinds ?? [])]
    .sort((left, right) => Number(left.sortOrder) - Number(right.sortOrder));
  const selectedWorkKind = workKinds.find((kind) => kind.normalizedLabel === form.work.workKind);
  const requiresCustomWorkKind = selectedWorkKind?.requiresCustomWorkKind ?? false;

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
        <h3>Kategorier</h3>
      </div>

      {isLoading && <p className="empty-state-text">Henter kategorier...</p>}

      {!isLoading && (
        <div className="work-category-form">
          <div className="work-field-group">
            <span className="work-field-label">Kategori</span>
            <div className="category-choice-grid">
              {categories.map((category) => (
                <button
                  key={category.id}
                  type="button"
                  className={`choice-card ${form.work.categoryIds.includes(category.id) ? 'selected' : ''}`}
                  onClick={() => toggleCategory(category.id)}
                >
                  <span>{category.name}</span>
                  {form.work.categoryIds.includes(category.id) && <CheckCircle2 size={16} />}
                </button>
              ))}
            </div>
            {form.work.categoryIds.length === 0 && (
              <span className="form-help-error">Vælg mindst én kategori.</span>
            )}
          </div>

          <div className="work-field-group">
            <span className="work-field-label">Arbejde</span>
            <div className="work-kind-list">
              {workKinds.map((workKind) => (
                <label key={workKind.normalizedLabel} className="work-kind-option">
                  <input
                    type="radio"
                    name="workKind"
                    value={workKind.normalizedLabel}
                    checked={form.work.workKind === workKind.normalizedLabel}
                    onChange={(event) => onWorkKindChange(event.target.value)}
                  />
                  <span>{workKind.label}</span>
                </label>
              ))}
            </div>
            {form.work.workKind.length === 0 && (
              <span className="form-help-error">Vælg en arbejdstype.</span>
            )}
          </div>

          {requiresCustomWorkKind && (
            <label className="work-field-group">
              <span className="work-field-label">Beskriv service andet</span>
              <input
                className="form-input"
                value={form.work.customWorkKind}
                onChange={(event) => onCustomWorkKindChange(event.target.value)}
                placeholder="Skriv hvilken service der udføres"
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
