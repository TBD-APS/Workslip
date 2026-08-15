import { useEffect, useState } from 'react';
import { MessageCircle } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { JobConversationDrawer } from './JobConversationDrawer';

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
  const [isOpen, setIsOpen] = useState(requestedOpen);

  useEffect(() => {
    if (requestedOpen) setIsOpen(true);
  }, [requestedOpen]);

  const open = () => {
    setIsOpen(true);
    const next = new URLSearchParams(searchParams);
    next.set('conversation', '1');
    setSearchParams(next, { replace: true });
  };

  const close = () => {
    setIsOpen(false);
    const next = new URLSearchParams(searchParams);
    next.delete('conversation');
    next.delete('message');
    setSearchParams(next, { replace: true });
  };

  return (
    <>
      <button
        type="button"
        className={className ?? (compact ? 'btn btn-secondary report-overview-icon-action' : 'btn btn-secondary')}
        onClick={open}
        aria-label="Åbn samtale om sagen"
        title="Samtale"
      >
        <MessageCircle size={compact ? 16 : 18} />
        {!compact && <span>Samtale</span>}
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
