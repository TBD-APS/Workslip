import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { JobStatusDots } from './JobStatusDots';

afterEach(cleanup);

describe('JobStatusDots', () => {
  it('shows all statuses and clearly marks the current status', () => {
    render(<JobStatusDots status={JobStatus.Draft} />);

    const currentStatus = screen.getByRole('button', { name: 'Aktiv, nuværende status' });
    expect(currentStatus).toHaveAttribute('aria-pressed', 'true');
    expect(currentStatus).toHaveAttribute('aria-current', 'step');
    expect(screen.getByText('Aktiv', { selector: '.job-status-current-label strong' })).toBeInTheDocument();
    expect(screen.getByText(/Aktuel status:/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Til gennemsyn, ikke tilgængelig' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Godkendt, ikke tilgængelig' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Afvist, ikke tilgængelig' })).toBeDisabled();
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

    expect(screen.getByText('Til gennemsyn', { selector: '.job-status-current-label strong' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Godkendt, ikke tilgængelig' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Afvist, ikke tilgængelig' })).toBeDisabled();
  });
});
