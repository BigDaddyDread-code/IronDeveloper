import { useState } from 'react';
import type { CreateProjectChannelRequest, ProjectChannelChatSummary, ProjectChatSession } from '../../api/types';
import { errorMessage } from './useProjectChannels';

interface ChatSessionRailProps {
  sessions: ProjectChatSession[];
  channels: ProjectChannelChatSummary[];
  activeSessionId: number | null;
  activeChannelSlug: string | null;
  canCreateChannels: boolean;
  channelLoadState: 'loading' | 'ready' | 'error';
  channelError: string | null;
  directNavigationDisabledReason?: string | null;
  isOpen: boolean;
  onClose: () => void;
  onOpenSession: (sessionId: number) => void;
  onOpenChannel: (slug: string) => void;
  onStartNewConversation: () => void;
  onCreateChannel: (request: CreateProjectChannelRequest) => Promise<ProjectChannelChatSummary>;
  onRetryChannels: () => void;
}

const groupOrder = ['Today', 'This Week', 'Earlier'];

export function ChatSessionRail({
  sessions,
  channels,
  activeSessionId,
  activeChannelSlug,
  canCreateChannels,
  channelLoadState,
  channelError,
  directNavigationDisabledReason = null,
  isOpen,
  onClose,
  onOpenSession,
  onOpenChannel,
  onStartNewConversation,
  onCreateChannel,
  onRetryChannels
}: ChatSessionRailProps) {
  const groupedSessions = groupSessions(sessions);
  const [isCreatingChannel, setIsCreatingChannel] = useState(false);
  const [channelName, setChannelName] = useState('');
  const [channelDescription, setChannelDescription] = useState('');
  const [channelVisibility, setChannelVisibility] = useState<'Project' | 'MembersOnly'>('Project');
  const [createError, setCreateError] = useState<string | null>(null);
  const [isSavingChannel, setIsSavingChannel] = useState(false);

  const submitChannel = async () => {
    if (!channelName.trim() || isSavingChannel) return;
    setIsSavingChannel(true);
    setCreateError(null);
    try {
      const created = await onCreateChannel({
        name: channelName.trim(),
        description: channelDescription.trim() || null,
        visibility: channelVisibility
      });
      setChannelName('');
      setChannelDescription('');
      setChannelVisibility('Project');
      setIsCreatingChannel(false);
      onOpenChannel(created.slug);
      onClose();
    } catch (error) {
      setCreateError(errorMessage(error, 'The channel could not be created.'));
    } finally {
      setIsSavingChannel(false);
    }
  };

  return (
    <>
      <button
        className={`chat-session-rail__scrim ${isOpen ? 'chat-session-rail__scrim--open' : ''}`.trim()}
        type="button"
        aria-label="Close conversations"
        tabIndex={isOpen ? 0 : -1}
        onClick={onClose}
      />
      <aside
        className={`chat-session-rail ${isOpen ? 'chat-session-rail--open' : ''}`.trim()}
        aria-label="Conversations"
        data-testid="chat.sessions"
      >
        <header className="chat-session-rail__header">
          <div>
            <h2>Conversations</h2>
            <p>Project channels and direct sessions</p>
          </div>
          <button
            className="chat-session-rail__new"
            type="button"
            aria-label="Start new conversation"
            title="Start new conversation"
            data-testid="chat.sessions.new"
            disabled={Boolean(directNavigationDisabledReason)}
            aria-describedby={directNavigationDisabledReason ? 'chat-direct-navigation-blocked' : undefined}
            onClick={onStartNewConversation}
          >
            <span aria-hidden="true">+</span>
          </button>
          <button className="chat-session-rail__close" type="button" onClick={onClose}>
            Close
          </button>
        </header>

        <div className="chat-session-rail__body">
          <section className="chat-session-rail__section" data-testid="chat.channels">
            <div className="chat-session-rail__section-heading">
              <h3>Project channels</h3>
              {canCreateChannels ? (
                <button
                  type="button"
                  className="chat-session-rail__section-action"
                  aria-label="Create project channel"
                  title="Create project channel"
                  data-testid="chat.channels.new.toggle"
                  onClick={() => setIsCreatingChannel((current) => !current)}
                >
                  <span aria-hidden="true">+</span>
                </button>
              ) : null}
            </div>
            {isCreatingChannel ? (
              <form
                className="chat-channel-create"
                data-testid="chat.channels.new.form"
                onSubmit={(event) => {
                  event.preventDefault();
                  void submitChannel();
                }}
              >
                <label>
                  Name
                  <input
                    value={channelName}
                    maxLength={100}
                    data-testid="chat.channels.new.name"
                    onChange={(event) => setChannelName(event.target.value)}
                    autoFocus
                  />
                </label>
                <label>
                  Description <span>Optional</span>
                  <input
                    value={channelDescription}
                    maxLength={500}
                    data-testid="chat.channels.new.description"
                    onChange={(event) => setChannelDescription(event.target.value)}
                  />
                </label>
                <label>
                  Visibility
                  <select
                    value={channelVisibility}
                    data-testid="chat.channels.new.visibility"
                    onChange={(event) => setChannelVisibility(event.target.value as 'Project' | 'MembersOnly')}
                  >
                    <option value="Project">Everyone in project</option>
                    <option value="MembersOnly">Members only</option>
                  </select>
                </label>
                {createError ? <p className="state-error">{createError}</p> : null}
                <div className="chat-channel-create__actions">
                  <button type="button" onClick={() => setIsCreatingChannel(false)}>Cancel</button>
                  <button
                    type="submit"
                    className="chat-channel-create__submit"
                    data-testid="chat.channels.new.submit"
                    disabled={!channelName.trim() || isSavingChannel}
                  >
                    {isSavingChannel ? 'Creating...' : 'Create'}
                  </button>
                </div>
              </form>
            ) : null}
            {channelLoadState === 'loading' ? <p className="chat-session-rail__empty">Loading channels...</p> : null}
            {channelLoadState === 'error' ? (
              <div className="chat-session-rail__error">
                <p>{channelError}</p>
                <button type="button" onClick={onRetryChannels}>Retry</button>
              </div>
            ) : null}
            {channelLoadState === 'ready' && channels.length === 0 ? (
              <p className="chat-session-rail__empty">No visible project channels.</p>
            ) : null}
            {channels.length > 0 ? (
              <nav className="chat-session-rail__channel-list" aria-label="Project channels">
                {channels.map((channel) => (
                  <button
                    key={channel.channelId}
                    type="button"
                    className={channel.slug === activeChannelSlug ? 'chat-session-rail__item chat-session-rail__item--active' : 'chat-session-rail__item'}
                    aria-current={channel.slug === activeChannelSlug ? 'page' : undefined}
                    data-testid={`chat.channels.item.${channel.slug}`}
                    onClick={() => {
                      onOpenChannel(channel.slug);
                      onClose();
                    }}
                  >
                    <span className="chat-session-rail__channel-title">
                      <span># {channel.name}</span>
                      {channel.unreadCount > 0 ? (
                        <span className="chat-session-rail__unread" aria-label={`${channel.unreadCount} unread`}>
                          {channel.unreadCount}
                        </span>
                      ) : null}
                    </span>
                    <small>{channel.visibility === 'MembersOnly' ? 'Members only' : 'Project'} / {channel.memberCount} member(s)</small>
                  </button>
                ))}
              </nav>
            ) : null}
          </section>

          <section className="chat-session-rail__section">
            <h3>Direct with Workshop guide</h3>
            {directNavigationDisabledReason ? (
              <p id="chat-direct-navigation-blocked" className="chat-session-rail__empty" data-testid="chat.sessions.boundReason">
                {directNavigationDisabledReason}
              </p>
            ) : null}
            {sessions.length === 0 ? (
              <p className="chat-session-rail__empty">No saved direct conversations yet.</p>
            ) : (
              <nav className="chat-session-rail__groups" aria-label="Recent direct conversations">
            {groupedSessions.map(([group, items]) => (
              <section key={group} className="chat-session-rail__group" aria-labelledby={`chat-session-group-${toId(group)}`}>
                <h3 id={`chat-session-group-${toId(group)}`}>{group}</h3>
                {items.map((item) => (
                  <button
                    key={item.id}
                    className={item.id === activeSessionId ? 'chat-session-rail__item chat-session-rail__item--active' : 'chat-session-rail__item'}
                    type="button"
                    aria-current={item.id === activeSessionId ? 'page' : undefined}
                    data-testid={`chat.sessions.item.${item.id}`}
                    disabled={Boolean(directNavigationDisabledReason && item.id !== activeSessionId)}
                    onClick={() => {
                      if (item.id) {
                        onOpenSession(item.id);
                        onClose();
                      }
                    }}
                  >
                    <span>{item.title?.trim() || 'Untitled conversation'}</span>
                    <time dateTime={item.updatedDate}>{formatUpdatedDate(item.updatedDate)}</time>
                  </button>
                ))}
              </section>
            ))}
              </nav>
            )}
          </section>
        </div>
      </aside>
    </>
  );
}

function groupSessions(sessions: ProjectChatSession[]) {
  const groups = new Map<string, ProjectChatSession[]>();

  for (const session of sessions.filter((item) => Number.isFinite(item.id))) {
    const group = session.dateGroup?.trim() || dateGroup(session.updatedDate);
    const items = groups.get(group) ?? [];
    items.push(session);
    groups.set(group, items);
  }

  return [...groups.entries()].sort(([left], [right]) => {
    const leftIndex = groupOrder.indexOf(left);
    const rightIndex = groupOrder.indexOf(right);
    return (leftIndex < 0 ? groupOrder.length : leftIndex) - (rightIndex < 0 ? groupOrder.length : rightIndex);
  });
}

function dateGroup(value: string | undefined) {
  if (!value) return 'Earlier';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Earlier';

  const today = new Date();
  const startOfToday = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime();
  const startOfDate = new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
  const days = Math.floor((startOfToday - startOfDate) / 86_400_000);
  if (days <= 0) return 'Today';
  if (days <= 7) return 'This Week';
  return 'Earlier';
}

function formatUpdatedDate(value: string | undefined) {
  if (!value) return 'Time unavailable';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Time unavailable';
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(date);
}

function toId(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-');
}
