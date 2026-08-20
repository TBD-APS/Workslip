import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ActionableValidationSummary } from './ActionableValidationSummary';
import type { JobValidationIssue } from './jobValidation';

const issues: JobValidationIssue[] = [
  {
    code: 'customer.name.required',
    field: 'customerSnapshot.name',
    message: 'Kundenavn mangler.',
    step: 0,
    targetId: 'customerName',
    actionLabel: 'Udfyld kundenavn',
  },
  {
    code: 'worksheets.required',
    field: 'worksheets',
    message: 'Der mangler en timeseddel.',
    step: 3,
    targetId: 'job-worksheet-add-trigger',
    actionLabel: 'Tilføj timeseddel',
  },
];

describe('ActionableValidationSummary', () => {
  it('shows every concrete validation message with its corrective action and wizard location', () => {
    render(<ActionableValidationSummary issues={issues} onAction={vi.fn()} />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('2 ting skal rettes')).toBeInTheDocument();
    expect(screen.getByText('Trin 1 · Stamdata')).toBeInTheDocument();
    expect(screen.getByText('Kundenavn mangler.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /udfyld kundenavn/i })).toBeInTheDocument();
    expect(screen.getByText('Trin 4 · Timesedler')).toBeInTheDocument();
    expect(screen.getByText('Der mangler en timeseddel.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /tilføj timeseddel/i })).toBeInTheDocument();
  });

  it('returns the selected semantic issue instead of page-specific navigation logic', () => {
    const onAction = vi.fn();
    render(<ActionableValidationSummary issues={issues} onAction={onAction} />);

    fireEvent.click(screen.getByRole('button', { name: /tilføj timeseddel/i }));

    expect(onAction).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledWith(issues[1]);
  });

  it('uses singular copy for one problem', () => {
    render(<ActionableValidationSummary issues={[issues[0]]} onAction={vi.fn()} />);
    expect(screen.getByText('1 ting skal rettes')).toBeInTheDocument();
  });

  it('renders nothing when there are no validation issues', () => {
    const { container } = render(<ActionableValidationSummary issues={[]} onAction={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });
});
