import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import { CommandButton } from '../../components/CommandButton';
import { MarkdownRenderer } from '../../components/MarkdownRenderer';
import type { ChatWorkspaceMessage } from './chatTypes';

interface ChatMessageProps {
  message: ChatWorkspaceMessage;
  onSaveDiscussion?: (messageId: string) => void;
  onViewSources?: (messageId: string) => void;
}

export function ChatMessage({ message, onSaveDiscussion, onViewSources }: ChatMessageProps) {
  const mode = message.response?.mode;
  const isFormalizationMode = mode === 'Formalization';
  const isExplorationMode = mode === 'Exploration' || !mode;
  const showGovernanceActions = Boolean(message.response?.showGovernanceActions);
  const hasSources = Boolean(
    message.response?.contextSummary ||
    message.response?.linkedFilePaths ||
    message.response?.linkedSymbols ||
    message.response?.traceId
  );
  const reasoningTrace = message.response?.reasoningTrace ?? [];
  const disambiguationQuestion = message.response?.disambiguationQuestion;

  return (
    <article className={`chat-message chat-message--${message.role}`} data-testid={`chat.message.${message.role}`}>
      <header className="chat-message__header">
        <span>{message.role === 'user' ? 'You' : 'IronDev'}</span>
        <div className="chat-message__meta">
          <time dateTime={message.createdUtc}>{DateTimeDisplay.toLocalDisplay(message.createdUtc)}</time>
        </div>
      </header>
      <MarkdownRenderer markdown={message.content} testId={`chat.message.${message.role}.markdown`} />
      {disambiguationQuestion ? (
        <div className="chat-message__disambiguation" data-testid="chat.message.disambiguation">
          <strong>Clarify:</strong> {disambiguationQuestion}
        </div>
      ) : null}
      {reasoningTrace.length > 0 ? (
        <details className="chat-message__reasoning" data-testid="chat.message.reasoning" open>
          <summary>Raw reasoning trace</summary>
          <ul>
            {reasoningTrace.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </details>
      ) : null}
      {message.response?.reasoningSummary ? (
        <p className="chat-message__reasoningSummary" data-testid="chat.message.reasoningSummary">
          {message.response.reasoningSummary}
        </p>
      ) : null}
      {message.role === 'assistant' ? (
        <div className="chat-message__actions" data-testid="chat.message.actions">
          {!isExplorationMode ? (
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.message.copyMarkdown"
              onClick={() => void navigator.clipboard?.writeText(message.content)}
            >
              Copy Markdown
            </CommandButton>
          ) : null}
          {showGovernanceActions && onSaveDiscussion ? (
            <CommandButton
              type="button"
              variant="secondary"
              testId="chat.message.saveDiscussion"
              onClick={() => onSaveDiscussion(message.id)}
              disabled={message.discussionSaveStatus === 'saving'}
            >
              {message.discussionSaveStatus === 'saving' ? 'Saving Discussion' : 'Save Discussion'}
            </CommandButton>
          ) : null}
          {isFormalizationMode && hasSources && onViewSources ? (
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.message.viewSources"
              onClick={() => onViewSources(message.id)}
            >
              View Sources
            </CommandButton>
          ) : null}
        </div>
      ) : null}
      {message.savedDiscussion ? (
        <div className="chat-message__saved-discussion" data-testid="chat.message.savedDiscussion">
          Discussion saved - Document {message.savedDiscussion.documentId} - Version {message.savedDiscussion.documentVersionId}
        </div>
      ) : null}
      {message.discussionSaveStatus === 'error' && message.discussionSaveError ? (
        <p className="state-error" data-testid="chat.message.saveDiscussionError">{message.discussionSaveError}</p>
      ) : null}
    </article>
  );
}
