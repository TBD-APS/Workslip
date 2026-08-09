import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ReferenceDataResponse } from '../../../../api/generated/models';
import { emptyForm } from '../../utils';
import { ControlPointsStep } from './ControlPointsStep';

const typeId = '00000000-0000-0000-0000-000000000001';
const categoryId = '00000000-0000-0000-0000-000000000002';
const referenceData = {
  workKinds: [],
  closureFlags: [],
  installationTypes: [{
    id: typeId,
    name: 'Vand',
    sortOrder: 1,
    categories: [{ id: categoryId, name: 'installation', sortOrder: 1, controlPoints: [] }],
  }],
} as ReferenceDataResponse;

const reasonLabel = 'Kommentar – hvorfor var ingen kontrolpunkter relevante?';

describe('ControlPointsStep', () => {
  it('only shows the shared comment at the bottom when every category is irrelevant', () => {
    const onReasonChange = vi.fn();
    const { rerender } = render(
      <ControlPointsStep
        form={{ ...emptyForm, work: { ...emptyForm.work, categoryIds: [typeId] } }}
        referenceData={referenceData}
        onToggleControlPoint={vi.fn()}
        onToggleCategoryIrrelevant={vi.fn()}
        onAllIrrelevantReasonChange={onReasonChange}
      />,
    );

    expect(screen.queryByLabelText(reasonLabel)).not.toBeInTheDocument();

    rerender(
      <ControlPointsStep
        form={{
          ...emptyForm,
          work: {
            ...emptyForm.work,
            categoryIds: [typeId],
            irrelevantCategoryIds: [`${typeId}-${categoryId}`],
            allIrrelevantReason: 'Ikke en del af opgaven',
          },
        }}
        referenceData={referenceData}
        onToggleControlPoint={vi.fn()}
        onToggleCategoryIrrelevant={vi.fn()}
        onAllIrrelevantReasonChange={onReasonChange}
      />,
    );

    const reason = screen.getByLabelText(reasonLabel);
    expect(reason).toHaveValue('Ikke en del af opgaven');
    fireEvent.change(reason, { target: { value: 'Ny begrundelse' } });
    expect(onReasonChange).toHaveBeenCalledWith('Ny begrundelse');
  });
});
