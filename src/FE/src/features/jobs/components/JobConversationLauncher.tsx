import { useState } from 'react';
import { MessageCircle } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { useGetApiJobsJobIdConversation } from '../../../api/generated/job-conversations/job-conversations';
import { JobConversationDrawer } from './JobConversationDrawer';
import './JobConversationLauncher.css';

type JobConversationLauncherProps = {
  jobId: string;
  allowSubmitForReview: boolean;
  compact?: boolean;
  className?: string;
};

export function JobConversationLauncher({
  jobId,
  allowSubmitForReview,
  compact = false,
  className,
}: JobConversationLauncherProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedOpen = searchParams.get('conversation') === '1';
  const messageId = searchParams.get('message');
  const [manuallyOpen, setManuallyOpen] = useState(false);
  const isOpen = requestedOpen || manuallyOpen;
  const conversation = useGetApiJobsJobIdConversation(jobId, undefined, {
    query: {
      enabled: jobId.length > 0,
      staleTime: 10_000,
      refetchInterval: 15_000,
      refetchIntervalInBackground: false,
    },
    request: { skipGlobalErrorToast: true },
  });
  const unreadCount = Number(conversation.data?.unreadCount ?? 0);
  const ariaLabel = unreadCount > 0
    ? `Åbn samtale om sagen, ${unreadCount} ulæst${unreadCount === 1 ? '' : 'e'}`
    : 'Åbn samtale om sagen';

  const open = () => {
    setManuallyOpen(true);
    const next = new URLSearchParams(searchParams);
    next.set('conversation', '1');
    setSearchParams(next, { replace: true });
  };

  const close = () => {
    setManuallyOpen(false);
    const next = new URLSearchParams(searchParams);
    next.delete('conversation');
    next.delete('message');
    setSearchParams(next, { replace: true });
  };

  return (
    <>
      <button
        type="button"
        className={`${className ?? (compact ? 'btn btn-secondary report-overview-icon-action' : 'btn btn-secondary')} job-conversation-launcher-button`}
        onClick={open}
        aria-label={ariaLabel}
        title={unreadCount > 0 ? `Samtale · ${unreadCount} ulæst${unreadCount === 1 ? '' : 'e'}` : 'Samtale'}
      >
        <MessageCircle size={compact ? 16 : 18} />
        {!compact && <span>Samtale</span>}
        {unreadCount > 0 && (
          <span className="job-conversation-unread" aria-hidden="true">
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
      </button>
      <JobConversationDrawer
        jobId={jobId}
        isOpen={isOpen}
        onClose={close}
        allowSubmitForReview={allowSubmitForReview}
        initialMessageId={messageId}
      />
    </>
  );
}
