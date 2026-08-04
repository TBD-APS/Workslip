import type { ComponentProps } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { JobStatusControl } from './JobStatusControl';

const { mutateAsync } = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
}));

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['/api/jobs', id],
  getGetApiJobsQueryKey: () => ['/api/jobs'],
  usePostApiJobsIdStatus: () => ({
    mutateAsync,
    isPending: false,
  }),
}));

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

function renderControl(queryClient: QueryClient, props: Partial<ComponentProps<typeof JobStatusControl>> = {}) {
  render(
    <QueryClientProvider client={queryClient}>
      <JobStatusControl
        jobId="job-1"
        reportNumber="SAG-1234"
        status={JobStatus.Draft}
        editable
        allowedStatuses={[JobStatus.InReview]}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('JobStatusControl', () => {
  afterEach(cleanup);

  beforeEach(() => {
    mutateAsync.mockReset();
  });

  it('renders the current status for read-only users without change buttons', () => {
    const queryClient = new QueryClient();

    renderControl(queryClient, { editable: false });

    expect(screen.getByLabelText('Aktiv (nuværende status)')).toHaveAttribute('aria-current', 'true');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('confirms submission, updates the detail cache, and invalidates detail and list queries', async () => {
    const updatedJob = { id: 'job-1', status: JobStatus.InReview };
    mutateAsync.mockResolvedValueOnce(updatedJob);
    const queryClient = new QueryClient();
    const setQueryData = vi.spyOn(queryClient, 'setQueryData');
    const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue(undefined);
    const beforeChange = vi.fn().mockResolvedValue(true);
    const onChanged = vi.fn();

    renderControl(queryClient, { beforeChange, onChanged });
    fireEvent.click(screen.getByRole('button', { name: 'Skift status: Til gennemsyn' }));

    expect(screen.getByRole('dialog', { name: 'Attestér og indsend sag' })).toHaveTextContent('Indsendelsen kan ikke fortrydes.');
    fireEvent.click(screen.getByRole('button', { name: 'Attestér og indsend' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(JobStatus.InReview));

    expect(beforeChange).toHaveBeenCalledWith(JobStatus.InReview);
    expect(mutateAsync).toHaveBeenCalledWith({
      id: 'job-1',
      data: { status: JobStatus.InReview },
    });
    expect(setQueryData).toHaveBeenCalledWith(['/api/jobs', 'job-1'], updatedJob);
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['/api/jobs', 'job-1'] });
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['/api/jobs'] });
  });

  it('blocks repeated status changes while pending changes are being saved', async () => {
    let resolveSave: (saved: boolean) => void = () => undefined;
    const beforeChange = vi.fn(() => new Promise<boolean>((resolve) => {
      resolveSave = resolve;
    }));
    const queryClient = new QueryClient();

    renderControl(queryClient, { beforeChange });
    fireEvent.click(screen.getByRole('button', { name: 'Skift status: Til gennemsyn' }));
    const confirmButton = screen.getByRole('button', { name: 'Attestér og indsend' });

    fireEvent.click(confirmButton);
    fireEvent.click(confirmButton);

    expect(beforeChange).toHaveBeenCalledTimes(1);
    expect(confirmButton).toBeDisabled();

    await act(async () => resolveSave(false));

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Attestér og indsend sag' })).not.toBeInTheDocument());
    expect(mutateAsync).not.toHaveBeenCalled();
  });
});
