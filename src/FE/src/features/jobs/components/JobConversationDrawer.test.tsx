import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  useGetApiJobsJobIdConversation,
  usePostApiJobsJobIdConversationMessages,
  usePostApiJobsJobIdConversationMessagesMessageIdResolve,
  usePostApiJobsJobIdConversationRead,
} from '../../../api/generated/job-conversations/job-conversations';
import { ConversationActionStatus, ConversationActionType } from '../../../api/generated/models';
import { JobConversationDrawer } from './JobConversationDrawer';

vi.mock('../../../api/generated/job-conversations/job-conversations', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/generated/job-conversations/job-conversations')>();
  return {
    ...actual,
    useGetApiJobsJobIdConversation: vi.fn(),
    usePostApiJobsJobIdConversationMessages: vi.fn(),
    usePostApiJobsJobIdConversationMessagesMessageIdResolve: vi.fn(),
    usePostApiJobsJobIdConversationRead: vi.fn(),
  };
});

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({ user: { id: 'user-current', organizationId: 'org-1' } }),
}));

vi.mock('../../../components/common/Drawer', () => ({
  Drawer: ({ children, isOpen }: { children: ReactNode; isOpen: boolean }) => isOpen ? <div>{children}</div> : null,
}));

vi.mock('../../../lib/toast', () => ({
  notify: { success: vi.fn(), error: vi.fn() },
}));

function createMutationMock() {
  return {
    mutate: vi.fn(),
    isPending: false,
    variables: undefined,
  };
}

function renderDrawer() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <JobConversationDrawer
        jobId="job-1"
        isOpen
        onClose={vi.fn()}
        allowSubmitForReview
      />
    </QueryClientProvider>,
  );
}

describe('JobConversationDrawer', () => {
  const sendMutation = createMutationMock();
  const resolveMutation = createMutationMock();
  const readMutation = createMutationMock();

  beforeEach(() => {
    vi.clearAllMocks();
    sendMutation.mutate = vi.fn();
    resolveMutation.mutate = vi.fn();
    readMutation.mutate = vi.fn();

    vi.mocked(usePostApiJobsJobIdConversationMessages).mockReturnValue(sendMutation as never);
    vi.mocked(usePostApiJobsJobIdConversationMessagesMessageIdResolve).mockReturnValue(resolveMutation as never);
    vi.mocked(usePostApiJobsJobIdConversationRead).mockReturnValue(readMutation as never);
    vi.mocked(useGetApiJobsJobIdConversation).mockReturnValue({
      data: {
        jobId: 'job-1',
        unreadCount: 0,
        participants: [
          { id: 'user-current', displayName: 'Rasmus' },
          { id: 'user-mikkel', displayName: 'Mikkel' },
        ],
        messages: [],
      },
      isPending: false,
      isError: false,
      refetch: vi.fn(),
    } as never);
  });

  it('turns mention and action buttons into one structured message request', () => {
    renderDrawer();

    fireEvent.click(screen.getByRole('button', { name: /Nævn/ }));
    fireEvent.click(screen.getByRole('button', { name: /Mikkel/ }));

    fireEvent.click(screen.getByRole('button', { name: /Handling/ }));
    fireEvent.click(screen.getByRole('button', { name: /Mikkel/ }));

    fireEvent.change(screen.getByLabelText('Skriv en besked'), {
      target: { value: 'Kan du lige bekræfte den her?' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));

    expect(sendMutation.mutate).toHaveBeenCalledWith(
      {
        jobId: 'job-1',
        data: {
          body: 'Kan du lige bekræfte den her?',
          mentionedUserIds: ['user-mikkel'],
          actionType: ConversationActionType.Acknowledge,
          actionTargetUserId: 'user-mikkel',
        },
      },
      expect.objectContaining({ onSuccess: expect.any(Function), onError: expect.any(Function) }),
    );
  });

  it('shows a real resolve button only to the action target', () => {
    vi.mocked(useGetApiJobsJobIdConversation).mockReturnValue({
      data: {
        jobId: 'job-1',
        unreadCount: 0,
        participants: [
          { id: 'user-current', displayName: 'Rasmus' },
          { id: 'user-admin', displayName: 'Admin' },
        ],
        messages: [{
          id: 'message-1',
          jobId: 'job-1',
          authorUserId: 'user-admin',
          authorDisplayName: 'Admin',
          body: 'Bekræft at du har set ændringen.',
          mentionedUserIds: ['user-current'],
          action: {
            type: ConversationActionType.Acknowledge,
            targetUserId: 'user-current',
            targetDisplayName: 'Rasmus',
            status: ConversationActionStatus.Pending,
            resolvedByUserId: null,
            resolvedByDisplayName: null,
            resolvedUtc: null,
          },
          createdUtc: '2026-08-15T19:00:00.000Z',
        }],
      },
      isPending: false,
      isError: false,
      refetch: vi.fn(),
    } as never);

    renderDrawer();

    fireEvent.click(screen.getByRole('button', { name: 'Bekræft' }));

    expect(resolveMutation.mutate).toHaveBeenCalledWith(
      { jobId: 'job-1', messageId: 'message-1' },
      expect.objectContaining({ onSuccess: expect.any(Function), onError: expect.any(Function) }),
    );
  });
});
