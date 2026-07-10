import type { ProjectChatSession } from '../../api/types';

interface ChatSessionRailProps {
  sessions: ProjectChatSession[];
  activeSessionId: number | null;
  isOpen: boolean;
  onClose: () => void;
  onOpenSession: (sessionId: number) => void;
  onStartNewConversation: () => void;
}

const groupOrder = ['Today', 'This Week', 'Earlier'];

export function ChatSessionRail({
  sessions,
  activeSessionId,
  isOpen,
  onClose,
  onOpenSession,
  onStartNewConversation
}: ChatSessionRailProps) {
  const groupedSessions = groupSessions(sessions);

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
            <p>Direct with IronDev</p>
          </div>
          <button
            className="chat-session-rail__new"
            type="button"
            aria-label="Start new conversation"
            title="Start new conversation"
            data-testid="chat.sessions.new"
            onClick={onStartNewConversation}
          >
            <span aria-hidden="true">+</span>
          </button>
          <button className="chat-session-rail__close" type="button" onClick={onClose}>
            Close
          </button>
        </header>

        {sessions.length === 0 ? (
          <p className="chat-session-rail__empty">No saved conversations yet.</p>
        ) : (
          <nav className="chat-session-rail__groups" aria-label="Recent conversations">
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
