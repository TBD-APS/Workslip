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

vi.mock('./CompletedJobReport', () => ({
  CompletedJobReport: () => <RouteProbe mode="report" />,
}));

vi.mock('./AdminCompletedJobReport', () => ({
  AdminCompletedJobReport: () => <RouteProbe mode="admin-reference" />,
}));

type RouteState = {
  from?: string;
  readOnly?: boolean;
  forceEdit?: boolean;
};

function renderRoute(path: string, state?: RouteState) {
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

  it.each([JobStatus.InReview, JobStatus.Approved])('keeps %s jobs on the existing report for admins', async (status) => {
    mocks.isAdmin = true;
    mocks.job = { status, jobType: 'KLS' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
    expect(screen.queryByText(/admin-reference/)).not.toBeInTheDocument();
  });

  it('routes rejected jobs to the isolated reference view for admins', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.Rejected, jobType: 'KLS' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('admin-reference:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });

  it('allows only rejected admins to force the reference view back into the editor', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.Rejected, jobType: 'KLS' };
    renderRoute('/app/completed/job-1', { from: '/app', forceEdit: true });

    expect(await screen.findByText('edit:/app/job/job-1:from=/app')).toBeInTheDocument();
  });

  it('does not let forceEdit bypass an InReview admin report', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.InReview, jobType: 'KLS' };
    renderRoute('/app/job/job-1', { from: '/app', forceEdit: true });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });

  it('keeps auditor-style read-only entry in the existing report even for a draft status', () => {
    mocks.isAdmin = true;
    renderRoute('/app/completed/job-1', { from: '/app/auditor', readOnly: true });

    expect(screen.getByText('report:/app/completed/job-1:from=/app/auditor')).toBeInTheDocument();
    expect(screen.queryByText(/admin-reference/)).not.toBeInTheDocument();
  });

  it('routes Diverse jobs to the existing report regardless of status', async () => {
    mocks.isAdmin = true;
    mocks.job = { status: JobStatus.Draft, jobType: 'Diverse' };
    renderRoute('/app/job/job-1', { from: '/app' });

    expect(await screen.findByText('report:/app/completed/job-1:from=/app')).toBeInTheDocument();
  });
});
