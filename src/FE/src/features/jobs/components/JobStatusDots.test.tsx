import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { JobStatusDots } from './JobStatusDots';

afterEach(cleanup);

describe('JobStatusDots', () => {
  it('shows exactly the four product statuses and marks the current status', () => {
    render(<JobStatusDots status={JobStatus.Draft} />);

    expect(screen.getAllByRole('button')).toHaveLength(4);
    expect(screen.getByRole('button', { name: 'Aktiv, nuværende status' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Til gennemsyn, ikke tilgængelig' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Godkendt, ikke tilgængelig' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Afvist, ikke tilgængelig' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /Genåbnet/ })).not.toBeInTheDocument();
  });

  it('presents a reopened job as active without adding a fifth dot', () => {
    render(<JobStatusDots status={JobStatus.Reopened} />);

    expect(screen.getAllByRole('button')).toHaveLength(4);
    expect(screen.getByRole('button', { name: 'Aktiv, nuværende status' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('button', { name: /Genåbnet/ })).not.toBeInTheDocument();
  });

  it('selects an enabled status', () => {
    const onStatusSelect = vi.fn();
    render(
      <JobStatusDots
        status={JobStatus.Draft}
        enabledStatuses={[JobStatus.InReview]}
        onStatusSelect={onStatusSelect}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Til gennemsyn, vælg status' }));

    expect(onStatusSelect).toHaveBeenCalledWith(JobStatus.InReview);
  });

  it('disables status changes while a transition is pending', () => {
    render(
      <JobStatusDots
        status={JobStatus.InReview}
        enabledStatuses={[JobStatus.Approved, JobStatus.Rejected]}
        isPending
        onStatusSelect={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Godkendt, ikke tilgængelig' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Afvist, ikke tilgængelig' })).toBeDisabled();
  });
});
