import type { ReferenceDataResponse } from '../../../../api/generated/models';
import type { JobForm } from '../../types';

export function validateControlPoints(
  form: JobForm,
  referenceData: ReferenceDataResponse | null,
): { valid: boolean; error?: string } {
  if (form.jobType === 'Diverse') return { valid: true };

  const selectedInstallationTypes = referenceData?.installationTypes.filter((t) => form.work.categoryIds.includes(t.id));

  for (const installationType of selectedInstallationTypes ?? []) {
    let hasAnyControlPoint = false;

    for (const cat of installationType.categories) {
      const compositeId = `${installationType.id}-${cat.id}`;
      const isIrrelevant = form.work.irrelevantCategoryIds.includes(compositeId);

      if (!isIrrelevant) {
        const hasSelectedControlPoint = cat.controlPoints?.some(
          (cp) => form.work.controlPointSelections[cp.id],
        );

        if (hasSelectedControlPoint) {
          hasAnyControlPoint = true;
        } else {
          return {
            valid: false,
            error: `Mindst et kontrolpunkt skal vælges for "${installationType.name} i ${capitalizeFirstLetter(cat.name)}"`,
          };
        }
      }
    }

    if (!hasAnyControlPoint) {
      return {
        valid: false,
        error: `Mindst én kategori i "${installationType.name}" skal have et kontrolpunkt valgt (kan ikke markere alle som "ikke relevant")`,
      };
    }
  }

  return { valid: true };
}

function capitalizeFirstLetter(str: string) {
  if (!str) return str;
  return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase();
}
