import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import type { ChatWorkspaceMessage } from './chatTypes';

interface ChatMessageProps {
  message: ChatWorkspaceMessage;
}

export function ChatMessage({ message }: ChatMessageProps) {
  return (
    <article className={`chat-message chat-message--${message.role}`} data-testid={`chat.message.${message.role}`}>
      <header className="chat-message__header">
        <span>{message.role === 'user' ? 'You' : 'IronDev'}</span>
        <time dateTime={message.createdUtc}>{DateTimeDisplay.toLocalDisplay(message.createdUtc)}</time>
      </header>
      <p>{message.content}</p>
      {message.response?.contextSummary ? (
        <div className="chat-message__context">
          <strong>Context Used</strong>
          <span>{message.response.contextSummary}</span>
        </div>
      ) : null}
    </article>
  );
}
