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

vi.mock('../components/JobAuditorScopeControl', () => ({
  JobAuditorScopeControl: ({ jobId }: { jobId: string }) => <div>{`auditor-scope:${jobId}`}</div>,
}));

function RouteProbe({ mode }: { mode: string }) {
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? 'none';
  return <div>{`${mode}:${location.pathname}:from=${from}`}</div>;
}

vi.mock('./JobDetail', () => ({
  JobDetail: () => <RouteProbe mode="edit" />,
}));

vi.mock('./CompletedJobReport', () => ({
  CompletedJobReport: () => <RouteProbe mode="report" />,
}));

function renderRoute(path: string, state?: { from?: string; readOnly?: boolean }) {
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

  it('redirects rejected jobs into the editor for normal users', async () => {
    mocks.job = { status: JobStatus.Rejected, jobType: 'KLS' };
    renderRoute('/app/completed/job-1', { from: '/app/customers/customer-1' });

    expect(await screen.findByText('edit:/app/job/job-1:from=/app/customers/customer-1')).toBeInTheDocument();
  });

  it.each([JobStatus.InReview, JobStatus.Approved])('routes %s jobs to the report', async (status) => {
    mocks.job = { status, jobType: 'KLS' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });

  it('routes rejected jobs to the report for admins and exposes the admin-only scope control', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.Rejected, jobType: 'KLS' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
    expect(screen.getByText('auditor-scope:job-1')).toBeInTheDocument();
  });

  it('keeps auditor-style read-only entry in the report even for a draft status', () => {
    renderRoute('/app/completed/job-1', { from: '/app/auditor', readOnly: true });

    expect(screen.getByText('report:/app/completed/job-1:from=/app/auditor')).toBeInTheDocument();
    expect(screen.queryByText('auditor-scope:job-1')).not.toBeInTheDocument();
  });

  it('routes Diverse jobs to the report regardless of status', async () => {
    mocks.job = { status: JobStatus.Draft, jobType: 'Diverse' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });
});
