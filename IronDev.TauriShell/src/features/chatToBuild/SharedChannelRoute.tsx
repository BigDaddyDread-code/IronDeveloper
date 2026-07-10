import { useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ProjectChannelChatDetail, ProjectChannelChatMessage, ProjectChatSession } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { RouteOutcomeScreen } from '../../flow/components/RouteOutcomeScreen';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { ChatSessionRail } from './ChatSessionRail';
import { errorMessage, useProjectChannels } from './useProjectChannels';

interface SharedChannelRouteProps {
  projectId: number;
  channelReference: string;
  onOpenChannel: (slug: string) => void;
  onOpenSession: (sessionId: number) => void;
  onOpenDirect: () => void;
}

export function SharedChannelRoute({
  projectId,
  channelReference,
  onOpenChannel,
  onOpenSession,
  onOpenDirect
}: SharedChannelRouteProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const channels = useProjectChannels();
  const [detail, setDetail] = useState<ProjectChannelChatDetail | null>(null);
  const [sessions, setSessions] = useState<ProjectChatSession[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'notFound' | 'unavailable'>('loading');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [draft, setDraft] = useState('');
  const [sendError, setSendError] = useState<string | null>(null);
  const [isSending, setIsSending] = useState(false);
  const [isRailOpen, setIsRailOpen] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoadState('loading');
    setLoadError(null);
    setSendError(null);
    setDraft('');

    Promise.all([
      session.client.getProjectChannel(projectId, channelReference, controller.signal),
      session.client.getProjectChatSessions(projectId, controller.signal).catch(() => [])
    ]).then(([channel, recentSessions]) => {
      setDetail(channel);
      setSessions(recentSessions);
      setLoadState('ready');
    }).catch((error) => {
      if (controller.signal.aborted) return;
      setDetail(null);
      setSessions([]);
      if (error instanceof IronDevApiError && error.status === 404) {
        setLoadState('notFound');
        return;
      }
      setLoadError(errorMessage(error, 'The shared channel could not be loaded.'));
      setLoadState('unavailable');
    });

    return () => controller.abort();
  }, [channelReference, projectId, reloadKey, session.client]);

  const postMessage = async () => {
    if (!draft.trim() || !detail?.channel.canPostMessages || isSending) return;
    setIsSending(true);
    setSendError(null);
    try {
      const saved = await session.client.postProjectChannelMessage(projectId, channelReference, draft.trim());
      setDetail((current) => current ? { ...current, messages: [...current.messages, saved] } : current);
      setDraft('');
    } catch (error) {
      setSendError(errorMessage(error, 'The message could not be posted.'));
    } finally {
      setIsSending(false);
    }
  };

  if (loadState === 'loading') {
    return <main className="chat-route-workspace fl-route-loading" data-testid="chat.channel.loading">Loading channel...</main>;
  }

  if (loadState === 'notFound') {
    return (
      <main className="chat-route-workspace" data-testid="chat.channel.route">
        <RouteOutcomeScreen
          kind="notFound"
          title="Channel not found"
          message="This channel does not exist or is not visible to you. Members-only channels do not disclose their contents."
          nextSafeAction="Open Chat and choose a channel returned by the backend."
          actionLabel="Open Chat"
          onAction={onOpenDirect}
        />
      </main>
    );
  }

  if (loadState === 'unavailable' || !detail) {
    return (
      <main className="chat-route-workspace" data-testid="chat.channel.route">
        <RouteOutcomeScreen
          kind="unavailable"
          title="Channel unavailable"
          message={loadError ?? 'The channel API did not return shared conversation state.'}
          nextSafeAction="Retry the backend-owned channel. Your current URL has been preserved."
          actionLabel="Retry"
          onAction={() => setReloadKey((value) => value + 1)}
        />
      </main>
    );
  }

  const channel = detail.channel;
  const readOnlyReason = channel.canPostMessages ? null : 'Your channel role is Read only. You cannot post messages.';

  return (
    <main className="chat-route-workspace" data-testid="chat.channel.route">
      <section className="chat-workspace-panel chat-channel-workspace" data-testid="chat.channel.workspace">
        <ChatSessionRail
          sessions={sessions}
          channels={channels.channels}
          activeSessionId={null}
          activeChannelSlug={channel.slug}
          canCreateChannels={channels.canCreateChannels}
          channelLoadState={channels.loadState}
          channelError={channels.error}
          isOpen={isRailOpen}
          onClose={() => setIsRailOpen(false)}
          onOpenSession={onOpenSession}
          onOpenChannel={onOpenChannel}
          onStartNewConversation={onOpenDirect}
          onCreateChannel={channels.createChannel}
          onRetryChannels={channels.retry}
        />
        <div className="chat-conversation">
          <header className="chat-page-header chat-channel-header">
            <div>
              <h1># {channel.name}</h1>
              <p>
                {project.selectedProjectName ?? `Project ${projectId}`} <span aria-hidden="true">/</span>{' '}
                {channel.visibility === 'MembersOnly' ? 'Members only' : 'Project'} / {channel.memberCount} member(s)
              </p>
              {channel.description ? <p className="chat-channel-header__description">{channel.description}</p> : null}
            </div>
            <CommandButton
              type="button"
              variant="subtle"
              className="chat-session-rail__toggle"
              testId="chat.sessions.toggle"
              onClick={() => setIsRailOpen(true)}
            >
              Conversations
            </CommandButton>
          </header>

          <div className="chat-channel-thread" data-testid="chat.channel.messages" aria-live="polite">
            {detail.messages.length === 0 ? (
              <div className="chat-channel-empty">
                <h2>No messages yet</h2>
                <p>Start the human conversation. Messages here do not approve work or change project state.</p>
              </div>
            ) : detail.messages.map((message) => <ChannelMessage key={message.messageId} message={message} />)}
          </div>

          <form
            className="chat-channel-composer"
            onSubmit={(event) => {
              event.preventDefault();
              void postMessage();
            }}
          >
            <label htmlFor="chat-channel-composer-input">Message #{channel.slug}</label>
            <textarea
              id="chat-channel-composer-input"
              data-testid="chat.channel.composer"
              value={draft}
              rows={3}
              maxLength={10000}
              disabled={!channel.canPostMessages || isSending}
              onChange={(event) => setDraft(event.target.value)}
            />
            <div className="chat-channel-composer__footer">
              <p data-testid="chat.channel.assistant-status">{readOnlyReason ?? detail.assistantParticipationStatus}</p>
              <CommandButton
                type="submit"
                variant="primary"
                testId="chat.channel.send"
                disabled={!draft.trim() || !channel.canPostMessages || isSending}
              >
                {isSending ? 'Sending...' : 'Send'}
              </CommandButton>
            </div>
            {sendError ? <p className="state-error" data-testid="chat.channel.error">{sendError}</p> : null}
          </form>
        </div>
      </section>
    </main>
  );
}

function ChannelMessage({ message }: { message: ProjectChannelChatMessage }) {
  return (
    <article className="chat-channel-message" data-testid={`chat.channel.message.${message.messageId}`}>
      <header>
        <strong>{message.authorDisplayName}</strong>
        <time dateTime={message.createdUtc}>{formatMessageTime(message.createdUtc)}</time>
      </header>
      <p>{message.message}</p>
      {message.status === 'Edited' ? <small>Edited</small> : null}
    </article>
  );
}

function formatMessageTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Time unavailable';
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}
