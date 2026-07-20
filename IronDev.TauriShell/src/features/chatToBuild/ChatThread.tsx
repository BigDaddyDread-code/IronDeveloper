import { useEffect, useRef } from 'react';
import type { ChatAgentRunState, ChatWorkspaceMessage } from './chatTypes';
import { ChatMessage } from './ChatMessage';

interface ChatThreadProps {
  messages: ChatWorkspaceMessage[];
  isSending: boolean;
  agentRun: ChatAgentRunState | null;
  onSaveDiscussion?: (messageId: string) => void;
  onViewSources?: (messageId: string) => void;
  onReviewProjectState: () => void;
  onStartDraft: (prompt: string) => void;
}

const starters = [
  { label: 'Shape a new feature', prompt: 'I want to shape a new feature: ' },
  { label: 'Investigate a problem', prompt: 'Help me investigate this problem: ' },
  { label: 'Create a ticket from an idea', prompt: 'Help me turn this idea into a ticket: ' }
];

export function ChatThread({
  messages,
  isSending,
  agentRun,
  onSaveDiscussion,
  onViewSources,
  onReviewProjectState,
  onStartDraft
}: ChatThreadProps) {
  const bottomRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({
      behavior: 'smooth',
      block: 'end'
    });
  }, [messages.length, isSending]);

  return (
    <section className="chat-thread" data-testid="chat.thread">
      {messages.length === 0 ? (
        <div className="chat-empty-state" data-testid="chat.emptyState">
          <h2>What would you like to work on?</h2>
          <p>Start with the current project, or describe the work in your own words.</p>
          <div className="chat-empty-state__starters" aria-label="Conversation starters">
            <button type="button" onClick={onReviewProjectState}>Review current project understanding</button>
            {starters.map((starter) => (
              <button key={starter.label} type="button" onClick={() => onStartDraft(starter.prompt)}>
                {starter.label}
              </button>
            ))}
          </div>
        </div>
      ) : (
        <div className="chat-thread__messages">
          {messages.map((message) => (
            <ChatMessage
              key={message.id}
              message={message}
              onSaveDiscussion={onSaveDiscussion}
              onViewSources={onViewSources}
            />
          ))}
          <div ref={bottomRef} />
        </div>
      )}
      {isSending ? (
        <p className="chat-thread__sending" data-testid="chat.sending">
          {agentRun?.status === 'Pending' ? 'Business Analyst queued...' : agentRun?.status === 'Running' ? 'Business Analyst is working...' : 'Sending...'}
        </p>
      ) : null}
    </section>
  );
}
