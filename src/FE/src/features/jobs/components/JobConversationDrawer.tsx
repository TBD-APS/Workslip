import { useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  AtSign,
  Check,
  CheckCheck,
  ChevronDown,
  CircleCheck,
  Clock3,
  ListTodo,
  Loader2,
  MessageCircle,
  Send,
  Sparkles,
  UserPlus,
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
  dueUtc?: string | null;
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
  const assignableUsers = conversation.data?.assignableUsers ?? EMPTY_PARTICIPANTS;
  const messages = conversation.data?.messages ?? EMPTY_MESSAGES;
  const unreadCount = Number(conversation.data?.unreadCount ?? 0);
  const participantById = useMemo(
    () => new Map(participants.map((participant) => [participant.id, participant])),
    [participants],
  );
  const currentParticipant = participantById.get(currentUserId) ?? null;

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
    if (composerAction?.type === ConversationActionType.CreateTask && !trimmedBody) {
      notify.error('Skriv hvad opgaven går ud på.');
      return;
    }

    sendMessage.mutate(
      {
        jobId,
        data: {
          body: trimmedBody || null,
          mentionedUserIds: selectedMentionIds,
          actionType: composerAction?.type ?? null,
          actionTargetUserId: composerAction?.targetUserId ?? null,
          actionDueUtc: composerAction?.dueUtc ?? null,
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
  const requiresBody = composerAction?.type === ConversationActionType.CreateTask;
  const canSend = (
    body.trim().length > 0
      || (composerAction !== null && !requiresBody)
  ) && !sendMessage.isPending;
  const hasActions = selectableParticipants.length > 0
    || assignableUsers.length > 0
    || currentParticipant !== null;

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
                <span className="conversation-action-draft__eyebrow">
                  {composerAction.type === ConversationActionType.RemindMe
                    ? 'Påmindelse til dig'
                    : `Handling til ${composerAction.targetDisplayName}`}
                </span>
                <strong>{getActionLabel(composerAction.type)}</strong>
                {composerAction.dueUtc && (
                  <span className="conversation-action-draft__eyebrow">
                    {formatDueDate(composerAction.dueUtc)}
                  </span>
                )}
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
            placeholder={composerAction?.type === ConversationActionType.CreateTask
              ? 'Skriv hvad opgaven går ud på...'
              : composerAction?.type === ConversationActionType.RemindMe
                ? 'Hvad skal Workslip minde dig om?'
                : 'Skriv til dem på sagen...'}
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
                disabled={!hasActions}
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
              assignableUsers={assignableUsers}
              currentParticipant={currentParticipant}
              allowSubmitForReview={allowSubmitForReview}
              onSelect={(action) => {
                setComposerAction(action);
                setActionsOpen(false);
                if (
                  action.type !== ConversationActionType.AssignSelf
                  && action.type !== ConversationActionType.RemindMe
                ) {
                  setSelectedMentionIds((current) =>
                    current.includes(action.targetUserId) ? current : [...current, action.targetUserId]);
                }
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
                : getActionIcon(message.action.type)}
            </div>
            <div className="conversation-action-card__content">
              <span>{message.action.type === ConversationActionType.RemindMe ? 'Til dig selv' : message.action.targetDisplayName}</span>
              <strong>{getActionLabel(message.action.type)}</strong>
              {message.action.dueUtc && (
                <small>Planlagt {formatDueDate(message.action.dueUtc)}</small>
              )}
              {message.action.status === ConversationActionStatus.Completed && (
                <small>
                  Udført{message.action.resolvedByDisplayName ? ` af ${message.action.resolvedByDisplayName}` : ''}
                </small>
              )}
            </div>
            {isTarget && isPendingAction && message.action.canResolve ? (
              <button
                type="button"
                className="btn btn-primary conversation-action-card__button"
                onClick={onResolve}
                disabled={isResolving}
              >
                {isResolving ? <Loader2 size={16} className="animate-spin" /> : <Check size={16} />}
                {getActionButtonLabel(message.action.type)}
              </button>
            ) : isTarget && isPendingAction && !message.action.canResolve ? (
              <span className="conversation-action-pending"><Clock3 size={15} /> Planlagt</span>
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
  assignableUsers,
  currentParticipant,
  allowSubmitForReview,
  onSelect,
}: {
  participants: ConversationParticipantResponse[];
  assignableUsers: ConversationParticipantResponse[];
  currentParticipant: ConversationParticipantResponse | null;
  allowSubmitForReview: boolean;
  onSelect: (action: ComposerAction) => void;
}) {
  const [actionType, setActionType] = useState<ConversationActionType>(ConversationActionType.Acknowledge);
  const targetUsers = actionType === ConversationActionType.AssignSelf ? assignableUsers : participants;

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
        <button
          type="button"
          className={actionType === ConversationActionType.CreateTask ? 'selected' : ''}
          onClick={() => setActionType(ConversationActionType.CreateTask)}
        >
          <ListTodo size={16} />
          Opret opgave
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
        {assignableUsers.length > 0 && (
          <button
            type="button"
            className={actionType === ConversationActionType.AssignSelf ? 'selected' : ''}
            onClick={() => setActionType(ConversationActionType.AssignSelf)}
          >
            <UserPlus size={16} />
            Bed om at tage sagen
          </button>
        )}
        {currentParticipant && (
          <button
            type="button"
            className={actionType === ConversationActionType.RemindMe ? 'selected' : ''}
            onClick={() => setActionType(ConversationActionType.RemindMe)}
          >
            <Clock3 size={16} />
            Påmind mig
          </button>
        )}
      </div>

      {actionType === ConversationActionType.RemindMe && currentParticipant ? (
        <>
          <strong className="conversation-picker__title">Hvornår?</strong>
          <div className="conversation-picker__buttons">
            {getReminderOptions().map((option) => (
              <button
                key={option.label}
                type="button"
                className="conversation-person-button"
                onClick={() => onSelect({
                  type: ConversationActionType.RemindMe,
                  targetUserId: currentParticipant.id,
                  targetDisplayName: currentParticipant.displayName,
                  dueUtc: option.dueUtc,
                })}
              >
                <Clock3 size={16} />
                <span>{option.label}</span>
                <ChevronDown className="conversation-action-select-arrow" size={15} />
              </button>
            ))}
          </div>
        </>
      ) : (
        <>
          <strong className="conversation-picker__title">
            {actionType === ConversationActionType.AssignSelf ? 'Hvem skal tage sagen?' : 'Hvem skal handle?'}
          </strong>
          <div className="conversation-picker__buttons">
            {targetUsers.map((participant) => (
              <button
                key={participant.id}
                type="button"
                className="conversation-person-button"
                onClick={() => onSelect({
                  type: actionType,
                  targetUserId: participant.id,
                  targetDisplayName: participant.displayName,
                  dueUtc: null,
                })}
              >
                <UserRound size={16} />
                <span>{participant.displayName}</span>
                <ChevronDown className="conversation-action-select-arrow" size={15} />
              </button>
            ))}
          </div>
        </>
      )}
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

function getReminderOptions() {
  const now = new Date();
  const inOneHour = new Date(now.getTime() + 60 * 60 * 1000);
  const inThreeHours = new Date(now.getTime() + 3 * 60 * 60 * 1000);
  const tomorrowMorning = new Date(now);
  tomorrowMorning.setDate(tomorrowMorning.getDate() + 1);
  tomorrowMorning.setHours(8, 0, 0, 0);

  return [
    { label: 'Om 1 time', dueUtc: inOneHour.toISOString() },
    { label: 'Om 3 timer', dueUtc: inThreeHours.toISOString() },
    { label: 'I morgen kl. 08', dueUtc: tomorrowMorning.toISOString() },
  ];
}

function formatDueDate(value: string) {
  return new Date(value).toLocaleString('da-DK', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function getActionIcon(type: ConversationActionType) {
  switch (type) {
    case ConversationActionType.CreateTask:
      return <ListTodo size={19} />;
    case ConversationActionType.RemindMe:
      return <Clock3 size={19} />;
    case ConversationActionType.AssignSelf:
      return <UserPlus size={19} />;
    case ConversationActionType.SubmitForReview:
      return <CircleCheck size={19} />;
    default:
      return <Sparkles size={19} />;
  }
}

function getActionLabel(type: ConversationActionType) {
  switch (type) {
    case ConversationActionType.SubmitForReview:
      return 'Send sagen til gennemgang';
    case ConversationActionType.CreateTask:
      return 'Opgave';
    case ConversationActionType.RemindMe:
      return 'Påmind mig om sagen';
    case ConversationActionType.AssignSelf:
      return 'Tag sagen';
    default:
      return 'Bekræft at du har set det';
  }
}

function getActionButtonLabel(type: ConversationActionType) {
  switch (type) {
    case ConversationActionType.SubmitForReview:
      return 'Send til gennemgang';
    case ConversationActionType.CreateTask:
      return 'Markér færdig';
    case ConversationActionType.RemindMe:
      return 'Færdig';
    case ConversationActionType.AssignSelf:
      return 'Tag sagen';
    default:
      return 'Bekræft';
  }
}

function getResolvedToast(type: ConversationActionType) {
  switch (type) {
    case ConversationActionType.SubmitForReview:
      return 'Sagen er sendt til gennemgang.';
    case ConversationActionType.CreateTask:
      return 'Opgaven er markeret færdig.';
    case ConversationActionType.RemindMe:
      return 'Påmindelsen er afsluttet.';
    case ConversationActionType.AssignSelf:
      return 'Sagen er overtaget.';
    default:
      return 'Modtagelse er bekræftet.';
  }
}
