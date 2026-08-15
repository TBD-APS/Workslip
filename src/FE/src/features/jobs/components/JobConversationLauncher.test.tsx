import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useGetApiJobsJobIdConversation } from '../../../api/generated/job-conversations/job-conversations';
import { JobConversationLauncher } from './JobConversationLauncher';

vi.mock('../../../api/generated/job-conversations/job-conversations', () => ({
  useGetApiJobsJobIdConversation: vi.fn(),
}));

vi.mock('./JobConversationDrawer', () => ({
  JobConversationDrawer: ({
    isOpen,
    initialMessageId,
  }: {
    isOpen: boolean;
    initialMessageId?: string | null;
  }) => isOpen ? <div data-testid="conversation-drawer">{initialMessageId ?? 'no-message'}</div> : null,
}));

describe('JobConversationLauncher', () => {
  beforeEach(() => {
    vi.mocked(useGetApiJobsJobIdConversation).mockReturnValue({
      data: {
        jobId: 'job-1',
        participants: [],
        messages: [],
        unreadCount: 0,
      },
    } as never);
  });

  it('opens the exact conversation message from a notification deep link', () => {
    render(
      <MemoryRouter initialEntries={['/app/job/job-1?conversation=1&message=message-42']}>
        <Routes>
          <Route
            path="/app/job/:id"
            element={(
              <JobConversationLauncher
                jobId="job-1"
                allowSubmitForReview
              />
            )}
          />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByTestId('conversation-drawer')).toHaveTextContent('message-42');
  });

  it('surfaces unread conversation activity before the drawer is opened', () => {
    vi.mocked(useGetApiJobsJobIdConversation).mockReturnValue({
      data: {
        jobId: 'job-1',
        participants: [],
        messages: [],
        unreadCount: 3,
      },
    } as never);

    render(
      <MemoryRouter initialEntries={['/app/job/job-1']}>
        <Routes>
          <Route
            path="/app/job/:id"
            element={(
              <JobConversationLauncher
                jobId="job-1"
                allowSubmitForReview
              />
            )}
          />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole('button', { name: 'Åbn samtale om sagen, 3 ulæste' })).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.queryByTestId('conversation-drawer')).not.toBeInTheDocument();
  });
});
