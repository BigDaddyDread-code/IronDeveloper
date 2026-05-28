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
      <div className="section-heading">
        <p className="eyebrow">Conversation</p>
        <h3>Active project thread</h3>
      </div>
      {messages.length === 0 ? (
        <EmptyState
          title="Start a project state review"
          body="Send notes for review, or use Review Project State to get a grounded summary from IronDev.Api."
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
