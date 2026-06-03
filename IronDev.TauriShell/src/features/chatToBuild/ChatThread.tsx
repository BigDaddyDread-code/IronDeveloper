import { EmptyState } from '../../components/EmptyState';
import type { ChatWorkspaceMessage } from './chatTypes';
import { ChatMessage } from './ChatMessage';

interface ChatThreadProps {
  messages: ChatWorkspaceMessage[];
  isSending: boolean;
  onSaveDiscussion?: (messageId: string) => void;
  onViewSources?: (messageId: string) => void;
}

export function ChatThread({ messages, isSending, onSaveDiscussion, onViewSources }: ChatThreadProps) {
  return (
    <section className="chat-thread" data-testid="chat.thread">
      {messages.length === 0 ? (
        <EmptyState
          title="Start a conversation with IronDev"
          body="Ask about the selected project, shape an idea, or save useful responses as project discussions."
        />
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
        </div>
      )}
      {isSending ? <p className="chat-thread__sending" data-testid="chat.sending">Sending...</p> : null}
    </section>
  );
}
