import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import { CommandButton } from '../../components/CommandButton';
import { MarkdownRenderer } from '../../components/MarkdownRenderer';
import type { ChatWorkspaceMessage } from './chatTypes';

interface ChatMessageProps {
  message: ChatWorkspaceMessage;
}

export function ChatMessage({ message }: ChatMessageProps) {
  return (
    <article className={`chat-message chat-message--${message.role}`} data-testid={`chat.message.${message.role}`}>
      <header className="chat-message__header">
        <span>{message.role === 'user' ? 'You' : 'IronDev'}</span>
        <div className="chat-message__meta">
          <time dateTime={message.createdUtc}>{DateTimeDisplay.toLocalDisplay(message.createdUtc)}</time>
          {message.role === 'assistant' ? (
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.message.copyMarkdown"
              onClick={() => void navigator.clipboard?.writeText(message.content)}
            >
              Copy Markdown
            </CommandButton>
          ) : null}
        </div>
      </header>
      <MarkdownRenderer markdown={message.content} testId={`chat.message.${message.role}.markdown`} />
      {message.response?.contextSummary ? (
        <div className="chat-message__context">
          <strong>Context Used</strong>
          <span>{message.response.contextSummary}</span>
        </div>
      ) : null}
    </article>
  );
}
