import { EmptyState } from '../../components/EmptyState';
import type { ChatWorkspaceMessage } from './chatTypes';
import { ChatMessage } from './ChatMessage';

interface ChatThreadProps {
  messages: ChatWorkspaceMessage[];
  isSending: boolean;
}

export function ChatThread({ messages, isSending }: ChatThreadProps) {
  return (
    <section className="chat-thread" data-testid="chat.thread">
      {messages.length === 0 ? (
        <EmptyState
          title="Start a conversation with IronDev"
          body="Ask about the selected project, review project state, or draft work to continue into Build."
        />
      ) : (
        <div className="chat-thread__messages">
          {messages.map((message) => (
            <ChatMessage key={message.id} message={message} />
          ))}
        </div>
      )}
      {isSending ? <p className="chat-thread__sending" data-testid="chat.sending">Sending...</p> : null}
    </section>
  );
}
