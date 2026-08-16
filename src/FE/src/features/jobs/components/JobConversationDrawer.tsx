import { useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  AtSign,
  Check,
  CheckCheck,
  ChevronDown,
  CircleCheck,
  Loader2,
  MessageCircle,
  Send,
  Sparkles,
  UserRound,
  X,
} from 'lucide-react';
import {
  getGetApiJobsJobIdConversationQueryKey,
  useGetApiJobsJobIdConversation,
  usePostApiJobsJobIdConversationMessages,
  usePostApiJobsJobIdConversationMessagesMessageIdResolve,
  usePostApiJobsJobIdConversationRead,
} from '../../../api/generated/job-conversations/job-conversations';
import {
  ConversationActionStatus,
  ConversationActionType,
  type ConversationMessageResponse,
  type ConversationParticipantResponse,
} from '../../../api/generated/models';
import { getGetApiJobsIdQueryKey } from '../../../api/generated/jobs/jobs';
import { Drawer } from '../../../components/common/Drawer';
import { formatRelativeActivityTime, getActivityInitials } from '../../../components/common/activityFeed';
import { notify } from '../../../lib/toast';
import { useAuth } from '../../../providers/useAuth';
import './JobConversationDrawer.css';

type JobConversationDrawerProps = {
  jobId: string;
  isOpen: boolean;
  onClose: () => void;
  allowSubmitForReview: boolean;
  initialMessageId?: string | null;
};

type ComposerAction = {
  type: ConversationActionType;
  targetUserId: string;
  targetDisplayName: string;
};

const EMPTY_PARTICIPANTS: ConversationParticipantResponse[] = [];
const EMPTY_MESSAGES: ConversationMessageResponse[] = [];

export function JobConversationDrawer({
  jobId,
  isOpen,
  onClose,
  allowSubmitForReview,
  initialMessageId,
}: JobConversationDrawerProps) {
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const currentUserId = user?.id ?? '';
  const [body, setBody] = useState('');
  const [selectedMentionIds, setSelectedMentionIds] = useState<string[]>([]);
  const [composerAction, setComposerAction] = useState<ComposerAction | null>(null);
  const [mentionsOpen, setMentionsOpen] = useState(false);
  const [actionsOpen, setActionsOpen] = useState(false);
  const readAttemptedRef = useRef(false);

  const conversation = useGetApiJobsJobIdConversation(jobId, undefined, {
    query: {
      enabled: isOpen && jobId.length > 0,
      staleTime: 10_000,
    },
    request: { skipGlobalErrorToast: true },
  });
  const markRead = usePostApiJobsJobIdConversationRead({
    request: { skipGlobalErrorToast: true },
  });
  const sendMessage = usePostApiJobsJobIdConversationMessages({
    request: { skipGlobalErrorToast: true },
  });
  const resolveAction = usePostApiJobsJobIdConversationMessagesMessageIdResolve({
    request: { skipGlobalErrorToast: true },
  });

  const participants = conversation.data?.participants ?? EMPTY_PARTICIPANTS;
  const messages = conversation.data?.messages ?? EMPTY_MESSAGES;
  const unreadCount = Number(conversation.data?.unreadCount ?? 0);
  const participantById = useMemo(
    () => new Map(participants.map((participant) => [participant.id, participant])),
    [participants],
  );

  useEffect(() => {
    if (!isOpen) {
      readAttemptedRef.current = false;
      return;
    }
    if (unreadCount <= 0) {
      readAttemptedRef.current = false;
      return;
    }
    if (!conversation.data || markRead.isPending || readAttemptedRef.current) return;

    readAttemptedRef.current = true;
    markRead.mutate(
      { jobId },
      {
        onSuccess: () => {
          queryClient.setQueryData(
            getGetApiJobsJobIdConversationQueryKey(jobId),
            { ...conversation.data, unreadCount: 0 },
          );
        },
      },
    );
  }, [conversation.data, isOpen, jobId, markRead, queryClient, unreadCount]);

  useEffect(() => {
    if (!isOpen || !initialMessageId || messages.length === 0) return;

    const timer = window.setTimeout(() => {
      const target = document.getElementById(`conversation-message-${initialMessageId}`);
      target?.scrollIntoView({ block: 'center', behavior: 'smooth' });
      target?.focus({ preventScroll: true });
    }, 120);

    return () => window.clearTimeout(timer);
  }, [initialMessageId, isOpen, messages.length]);

  const resetComposer = () => {
    setBody('');
    setSelectedMentionIds([]);
    setComposerAction(null);
    setMentionsOpen(false);
    setActionsOpen(false);
  };

  const submit = () => {
    const trimmedBody = body.trim();
    if (!trimmedBody && !composerAction) return;

    sendMessage.mutate(
      {
        jobId,
        data: {
          body: trimmedBody || null,
          mentionedUserIds: selectedMentionIds,
          actionType: composerAction?.type ?? null,
          actionTargetUserId: composerAction?.targetUserId ?? null,
        },
      },
      {
        onSuccess: async () => {
          resetComposer();
          await queryClient.invalidateQueries({
            queryKey: getGetApiJobsJobIdConversationQueryKey(jobId),
          });
        },
        onError: () => notify.error('Beskeden kunne ikke sendes.'),
      },
    );
  };

  const resolve = (message: ConversationMessageResponse) => {
    if (!message.action || message.action.targetUserId !== currentUserId) return;

    resolveAction.mutate(
      { jobId, messageId: message.id },
      {
        onSuccess: async () => {
          await Promise.all([
            queryClient.invalidateQueries({ queryKey: getGetApiJobsJobIdConversationQueryKey(jobId) }),
            queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) }),
          ]);
          notify.success(getResolvedToast(message.action!.type));
        },
        onError: () => notify.error('Handlingen kunne ikke udføres.'),
      },
    );
  };

  const toggleMention = (participantId: string) => {
    setSelectedMentionIds((current) =>
      current.includes(participantId)
        ? current.filter((id) => id !== participantId)
        : [...current, participantId]);
  };

  const selectableParticipants = participants.filter((participant) => participant.id !== currentUserId);
  const canSend = (body.trim().length > 0 || composerAction !== null) && !sendMessage.isPending;

  return (
    <Drawer
      isOpen={isOpen}
      onClose={onClose}
      title="Samtale"
      ariaLabel="Samtale om sagen"
      icon={<MessageCircle size={20} />}
      className="job-conversation-drawer"
    >
      <div className="conversation-shell">
        <div className="conversation-context">
          <div>
            <strong>Sagens samtale</strong>
            <span>{participants.length} {participants.length === 1 ? 'deltager' : 'deltagere'}</span>
          </div>
          {unreadCount > 0 && (
            <span className="conversation-unread-badge">
              {unreadCount} ulæst{unreadCount === 1 ? '' : 'e'}
            </span>
          )}
        </div>

        <div className="conversation-feed" aria-live="polite">
          {conversation.isPending ? (
            <ConversationSkeleton />
          ) : conversation.isError ? (
            <div className="conversation-state conversation-state--error" role="alert">
              <strong>Kunne ikke hente samtalen</strong>
              <button type="button" className="btn btn-secondary" onClick={() => void conversation.refetch()}>
                Prøv igen
              </button>
            </div>
          ) : messages.length === 0 ? (
            <div className="conversation-state">
              <span className="conversation-state__icon" aria-hidden="true">
                <MessageCircle size={26} />
              </span>
              <strong>Start samtalen</strong>
              <span>Spørg en kollega, nævn en deltager eller send en konkret handling.</span>
            </div>
          ) : (
            messages.map((message) => (
              <ConversationMessage
                key={message.id}
                message={message}
                currentUserId={currentUserId}
                participantById={participantById}
                isResolving={resolveAction.isPending && resolveAction.variables?.messageId === message.id}
                onResolve={() => resolve(message)}
              />
            ))
          )}
        </div>

        <div className="conversation-composer">
          {selectedMentionIds.length > 0 && (
            <div className="conversation-selected-chips" aria-label="Nævnte deltagere">
              {selectedMentionIds.map((participantId) => {
                const participant = participantById.get(participantId);
                if (!participant) return null;
                return (
                  <button
                    key={participantId}
                    type="button"
                    className="conversation-chip conversation-chip--selected"
                    onClick={() => toggleMention(participantId)}
                    aria-label={`Fjern ${participant.displayName} fra nævnte`}
                  >
                    @{participant.displayName}
                    <X size={13} />
                  </button>
                );
              })}
            </div>
          )}

          {composerAction && (
            <div className="conversation-action-draft">
              <div>
                <span className="conversation-action-draft__eyebrow">Handling til {composerAction.targetDisplayName}</span>
                <strong>{getActionLabel(composerAction.type)}</strong>
              </div>
              <button
                type="button"
                className="btn-icon"
                onClick={() => setComposerAction(null)}
                aria-label="Fjern handling"
              >
                <X size={17} />
              </button>
            </div>
          )}

          <label className="sr-only" htmlFor={`job-conversation-${jobId}`}>Skriv en besked</label>
          <textarea
            id={`job-conversation-${jobId}`}
            className="form-input form-textarea conversation-textarea"
            value={body}
            onChange={(event) => setBody(event.target.value)}
            onKeyDown={(event) => {
              if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
                event.preventDefault();
                submit();
              }
            }}
            placeholder="Skriv til dem på sagen..."
            rows={3}
            maxLength={4000}
            disabled={sendMessage.isPending}
          />

          <div className="conversation-composer-actions">
            <div className="conversation-tool-group">
              <button
                type="button"
                className={`conversation-tool-button${mentionsOpen ? ' active' : ''}`}
                onClick={() => {
                  setMentionsOpen((current) => !current);
                  setActionsOpen(false);
                }}
                aria-expanded={mentionsOpen}
                disabled={selectableParticipants.length === 0}
              >
                <AtSign size={17} />
                Nævn
              </button>
              <button
                type="button"
                className={`conversation-tool-button${actionsOpen ? ' active' : ''}`}
                onClick={() => {
                  setActionsOpen((current) => !current);
                  setMentionsOpen(false);
                }}
                aria-expanded={actionsOpen}
                disabled={selectableParticipants.length === 0}
              >
                <Sparkles size={17} />
                Handling
              </button>
            </div>

            <button
              type="button"
              className="btn btn-primary conversation-send-button"
              onClick={submit}
              disabled={!canSend}
            >
              {sendMessage.isPending ? <Loader2 size={17} className="animate-spin" /> : <Send size={17} />}
              Send
            </button>
          </div>

          {mentionsOpen && (
            <ParticipantPicker
              title="Hvem vil du nævne?"
              participants={selectableParticipants}
              selectedIds={selectedMentionIds}
              onToggle={toggleMention}
            />
          )}

          {actionsOpen && (
            <ActionPicker
              participants={selectableParticipants}
              allowSubmitForReview={allowSubmitForReview}
              onSelect={(action) => {
                setComposerAction(action);
                setActionsOpen(false);
                setSelectedMentionIds((current) =>
                  current.includes(action.targetUserId) ? current : [...current, action.targetUserId]);
              }}
            />
          )}
        </div>
      </div>
    </Drawer>
  );
}

function ConversationMessage({
  message,
  currentUserId,
  participantById,
  isResolving,
  onResolve,
}: {
  message: ConversationMessageResponse;
  currentUserId: string;
  participantById: Map<string, ConversationParticipantResponse>;
  isResolving: boolean;
  onResolve: () => void;
}) {
  const isOwn = message.authorUserId === currentUserId;
  const isTarget = message.action?.targetUserId === currentUserId;
  const isPendingAction = message.action?.status === ConversationActionStatus.Pending;
  const mentionedNames = message.mentionedUserIds
    .map((id) => participantById.get(id)?.displayName)
    .filter((name): name is string => Boolean(name));

  return (
    <article
      id={`conversation-message-${message.id}`}
      className={`conversation-message${isOwn ? ' conversation-message--own' : ''}`}
      tabIndex={-1}
    >
      <span className="conversation-avatar" aria-hidden="true">
        {getActivityInitials(message.authorDisplayName)}
      </span>
      <div className="conversation-message__main">
        <div className="conversation-message__header">
          <strong>{isOwn ? 'Dig' : message.authorDisplayName}</strong>
          <time dateTime={message.createdUtc} title={new Date(message.createdUtc).toLocaleString('da-DK')}>
            {formatRelativeActivityTime(message.createdUtc)}
          </time>
        </div>

        {message.body && <p className="conversation-message__body">{message.body}</p>}

        {mentionedNames.length > 0 && (
          <div className="conversation-message__mentions" aria-label="Nævnte deltagere">
            {mentionedNames.map((name) => <span key={name}>@{name}</span>)}
          </div>
        )}

        {message.action && (
          <div className={`conversation-action-card${message.action.status === ConversationActionStatus.Completed ? ' completed' : ''}`}>
            <div className="conversation-action-card__icon" aria-hidden="true">
              {message.action.status === ConversationActionStatus.Completed
                ? <CircleCheck size={19} />
                : <Sparkles size={19} />}
            </div>
            <div className="conversation-action-card__content">
              <span>{message.action.targetDisplayName}</span>
              <strong>{getActionLabel(message.action.type)}</strong>
              {message.action.status === ConversationActionStatus.Completed && (
                <small>
                  Udført{message.action.resolvedByDisplayName ? ` af ${message.action.resolvedByDisplayName}` : ''}
                </small>
              )}
            </div>
            {isTarget && isPendingAction ? (
              <button
                type="button"
                className="btn btn-primary conversation-action-card__button"
                onClick={onResolve}
                disabled={isResolving}
              >
                {isResolving ? <Loader2 size={16} className="animate-spin" /> : <Check size={16} />}
                {getActionButtonLabel(message.action.type)}
              </button>
            ) : message.action.status === ConversationActionStatus.Completed ? (
              <span className="conversation-action-completed"><CheckCheck size={15} /> Udført</span>
            ) : (
              <span className="conversation-action-pending">Afventer</span>
            )}
          </div>
        )}
      </div>
    </article>
  );
}

function ParticipantPicker({
  title,
  participants,
  selectedIds,
  onToggle,
}: {
  title: string;
  participants: ConversationParticipantResponse[];
  selectedIds: string[];
  onToggle: (id: string) => void;
}) {
  return (
    <div className="conversation-picker">
      <strong className="conversation-picker__title">{title}</strong>
      <div className="conversation-picker__buttons">
        {participants.map((participant) => {
          const selected = selectedIds.includes(participant.id);
          return (
            <button
              key={participant.id}
              type="button"
              className={`conversation-person-button${selected ? ' selected' : ''}`}
              onClick={() => onToggle(participant.id)}
              aria-pressed={selected}
            >
              <span className="conversation-person-avatar" aria-hidden="true">
                {getActivityInitials(participant.displayName)}
              </span>
              <span>{participant.displayName}</span>
              {selected && <Check size={15} />}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function ActionPicker({
  participants,
  allowSubmitForReview,
  onSelect,
}: {
  participants: ConversationParticipantResponse[];
  allowSubmitForReview: boolean;
  onSelect: (action: ComposerAction) => void;
}) {
  const [actionType, setActionType] = useState<ConversationActionType>(ConversationActionType.Acknowledge);

  return (
    <div className="conversation-picker conversation-action-picker">
      <strong className="conversation-picker__title">Hvad skal der ske?</strong>
      <div className="conversation-action-type-buttons">
        <button
          type="button"
          className={actionType === ConversationActionType.Acknowledge ? 'selected' : ''}
          onClick={() => setActionType(ConversationActionType.Acknowledge)}
        >
          <CheckCheck size={16} />
          Bekræft modtaget
        </button>
        {allowSubmitForReview && (
          <button
            type="button"
            className={actionType === ConversationActionType.SubmitForReview ? 'selected' : ''}
            onClick={() => setActionType(ConversationActionType.SubmitForReview)}
          >
            <CircleCheck size={16} />
            Send til gennemgang
          </button>
        )}
      </div>

      <strong className="conversation-picker__title">Hvem skal handle?</strong>
      <div className="conversation-picker__buttons">
        {participants.map((participant) => (
          <button
            key={participant.id}
            type="button"
            className="conversation-person-button"
            onClick={() => onSelect({
              type: actionType,
              targetUserId: participant.id,
              targetDisplayName: participant.displayName,
            })}
          >
            <UserRound size={16} />
            <span>{participant.displayName}</span>
            <ChevronDown className="conversation-action-select-arrow" size={15} />
          </button>
        ))}
      </div>
    </div>
  );
}

function ConversationSkeleton() {
  return (
    <div className="conversation-skeleton" aria-label="Henter samtale">
      <span />
      <span />
      <span />
    </div>
  );
}

function getActionLabel(type: ConversationActionType) {
  return type === ConversationActionType.SubmitForReview
    ? 'Send sagen til gennemgang'
    : 'Bekræft at du har set det';
}

function getActionButtonLabel(type: ConversationActionType) {
  return type === ConversationActionType.SubmitForReview ? 'Send til gennemgang' : 'Bekræft';
}

function getResolvedToast(type: ConversationActionType) {
  return type === ConversationActionType.SubmitForReview
    ? 'Sagen er sendt til gennemgang.'
    : 'Modtagelse er bekræftet.';
}
