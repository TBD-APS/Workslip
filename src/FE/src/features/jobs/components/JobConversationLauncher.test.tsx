import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { JobConversationLauncher } from './JobConversationLauncher';

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
});
