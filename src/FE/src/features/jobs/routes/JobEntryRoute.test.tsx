import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { JobEntryRoute } from './JobEntryRoute';

const mocks = vi.hoisted(() => ({
  isAdmin: false,
  job: { status: 'Draft', jobType: 'KLS' } as { status: JobStatus; jobType: string },
}));

vi.mock('../../../api/generated/jobs/jobs', () => ({
  useGetApiJobsId: () => ({
    data: mocks.job,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => mocks.isAdmin,
}));

function RouteProbe({ mode }: { mode: string }) {
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? 'none';
  return <div>{`${mode}:${location.pathname}:from=${from}`}</div>;
}

vi.mock('./JobDetail', () => ({
  JobDetail: () => <RouteProbe mode="edit" />,
}));

// The completed-job overview is the single report surface for every state and viewer.
vi.mock('./AdminCompletedJobReport', () => ({
  AdminCompletedJobReport: () => <RouteProbe mode="report" />,
}));

function renderRoute(
  path: string,
  state?: { from?: string; readOnly?: boolean; forceEdit?: boolean },
) {
  render(
    <MemoryRouter initialEntries={[{ pathname: path, state }]}>
      <Routes>
        <Route path="/app/job/:id" element={<JobEntryRoute />} />
        <Route path="/app/completed/:id" element={<JobEntryRoute />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('JobEntryRoute', () => {
  afterEach(cleanup);

  beforeEach(() => {
    mocks.isAdmin = false;
    mocks.job = { status: JobStatus.Draft, jobType: 'KLS' };
  });

  it('redirects draft jobs from report links into the editor and preserves source state', async () => {
    renderRoute('/app/completed/job-1', { from: '/app/timer' });

    expect(await screen.findByText('edit:/app/job/job-1:from=/app/timer')).toBeInTheDocument();
  });

  // The completed view route is the read surface for every post-authoring state and viewer.
  it.each([JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected, JobStatus.Reopened])(
    'shows the report for %s jobs on the completed view route',
    async (status) => {
      mocks.job = { status, jobType: 'KLS' };
      renderRoute('/app/completed/job-1', { from: '/app' });

      expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
    },
  );

  it.each([JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected, JobStatus.Reopened])(
    'shows the report for %s jobs for admins on the view route',
    async (status) => {
      mocks.isAdmin = true;
      mocks.job = { status, jobType: 'KLS' };
      renderRoute('/app/completed/job-1', { from: '/app' });

      expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
    },
  );

  // Locked states cannot be edited, so the edit route falls back to the report.
  it.each([JobStatus.InReview, JobStatus.Approved])(
    'redirects locked %s jobs from the edit route to the report',
    async (status) => {
      mocks.job = { status, jobType: 'KLS' };
      renderRoute('/app/job/job-1', { from: '/app' });

      expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
    },
  );

  // Editable states (a case handed back for correction) open the wizard on the edit route,
  // so the assignee — admin or not — can correct and resubmit it.
  it.each([JobStatus.Rejected, JobStatus.Reopened])(
    'opens the wizard for editable %s jobs on the edit route',
    async (status) => {
      mocks.job = { status, jobType: 'KLS' };
      renderRoute('/app/job/job-1', { from: '/app' });

      expect(await screen.findByText('edit:/app/job/job-1:from=/app')).toBeInTheDocument();
    },
  );

  it('lets an admin force-edit a rejected case from the overview', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.Rejected, jobType: 'KLS' };
    renderRoute('/app/completed/job-1', { from: '/app', forceEdit: true });

    expect(await screen.findByText('edit:/app/job/job-1:from=/app')).toBeInTheDocument();
  });

  it('ignores force-edit for non-admins and keeps them on the report', async () => {
    mocks.isAdmin = false;
    mocks.job = { status: JobStatus.Rejected, jobType: 'KLS' };
    renderRoute('/app/completed/job-1', { from: '/app', forceEdit: true });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });

  it('ignores force-edit for approved cases and keeps the report locked', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.Approved, jobType: 'KLS' };
    renderRoute('/app/completed/job-1', { from: '/app', forceEdit: true });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });

  it('keeps auditor-style read-only entry in the report even for a draft status', () => {
    renderRoute('/app/completed/job-1', { from: '/app/auditor', readOnly: true });

    expect(screen.getByText('report:/app/completed/job-1:from=/app/auditor')).toBeInTheDocument();
  });

  it('routes Diverse jobs to the report regardless of status', async () => {
    mocks.job = { status: JobStatus.Draft, jobType: 'Diverse' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });
});
