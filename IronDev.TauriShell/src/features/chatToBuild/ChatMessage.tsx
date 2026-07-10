import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import { CommandButton } from '../../components/CommandButton';
import { MarkdownRenderer } from '../../components/MarkdownRenderer';
import { getChatModeGate } from './chatGovernanceGate';
import type { ChatAuditSource } from '../../api/types';
import type { ChatWorkspaceMessage } from './chatTypes';

interface ChatMessageProps {
  message: ChatWorkspaceMessage;
  onSaveDiscussion?: (messageId: string) => void;
  onViewSources?: (messageId: string) => void;
}

export function ChatMessage({ message, onSaveDiscussion, onViewSources }: ChatMessageProps) {
  const gate = getChatModeGate(message.response);
  const mode = gate.mode;
  const documentSources = message.documentSources ?? message.response?.documentSources ?? [];
  const hasSources = Boolean(
    documentSources.length > 0 ||
    message.response?.contextSummary ||
    message.response?.linkedFilePaths ||
    message.response?.linkedSymbols ||
    message.response?.traceId
  );
  const canViewSources = gate.canViewSources && hasSources;
  const reasoningTrace = message.response?.reasoningTrace ?? [];
  const disambiguationQuestion = message.response?.disambiguationQuestion;
  const clarificationQuestions = message.response?.clarification?.questions?.filter(Boolean) ?? [];
  const modeConfidence = gate.confidence;
  const modeReason = gate.reason;
  const auditSourceLabel = formatAuditSource(message.response?.auditSource);
  const reasoningSummary = message.response?.reasoningSummary;
  const routeSource = message.response?.routeSource;
  const routeChallenge = message.response?.routeChallenge;

  return (
    <article className={`chat-message chat-message--${message.role}`} data-testid={`chat.message.${message.role}`}>
      <header className="chat-message__header">
        <span>{message.role === 'user' ? 'You' : 'IronDev'}</span>
        <div className="chat-message__meta">
          <time dateTime={message.createdUtc}>{DateTimeDisplay.toLocalDisplay(message.createdUtc)}</time>
        </div>
      </header>
      <MarkdownRenderer markdown={message.content} testId={`chat.message.${message.role}.markdown`} />
      {documentSources.length > 0 ? (
        <div className="chat-message__document-sources" data-testid="chat.message.documentSources">
          {documentSources.map((source) => (
            <span key={source.documentVersionId}>
              <strong>{source.title}</strong>
              <small>{source.versionLabel}</small>
            </span>
          ))}
        </div>
      ) : null}
      {reasoningTrace.length > 0 ||
      auditSourceLabel ||
      message.response?.auditFallbackReason ||
      reasoningSummary ||
      mode ||
      modeReason ||
      clarificationQuestions.length > 0 ||
      disambiguationQuestion ? (
        <details className="chat-message__reasoning" data-testid="chat.message.reasoning">
          <summary>Audit / reasoning trace</summary>
          {mode ? (
            <p className="chat-message__mode" data-testid="chat.message.mode">
              <strong>Mode:</strong> {mode}
              {typeof modeConfidence === 'number' ? ` (${Math.round(modeConfidence * 100)}%)` : null}
            </p>
          ) : null}
          {modeReason ? (
            <p className="chat-message__modeReason" data-testid="chat.message.modeReason">
              <strong>Mode reason:</strong> {modeReason}
            </p>
          ) : null}
          {clarificationQuestions.length > 0 || disambiguationQuestion ? (
            <div className="chat-message__disambiguation" data-testid="chat.message.disambiguation">
              <strong>Clarification needed:</strong>
              {clarificationQuestions.length > 0 ? (
                <ul>
                  {clarificationQuestions.map((question) => (
                    <li key={question}>{question}</li>
                  ))}
                </ul>
              ) : (
                <p>{disambiguationQuestion}</p>
              )}
            </div>
          ) : null}
          {auditSourceLabel ? (
            <p className="chat-message__auditSource" data-testid="chat.message.auditSource">
              <strong>Audit source:</strong> {auditSourceLabel}
              {message.response?.auditHasFallbackEvidence ? ' (fallback evidence present)' : null}
            </p>
          ) : null}
          {routeSource ? (
            <p className="chat-message__routeSource" data-testid="chat.message.routeSource">
              <strong>Route source:</strong> {routeSource}
            </p>
          ) : null}
          {routeChallenge ? (
            <p className="chat-message__routeChallenge" data-testid="chat.message.routeChallenge">
              <strong>Route challenge:</strong> {routeChallenge.suggestedMode ?? 'unknown'} / {routeChallenge.suggestedRequestKind ?? 'unknown'}
              {typeof routeChallenge.confidence === 'number' ? ` (${Math.round(routeChallenge.confidence * 100)}%)` : null}
              {routeChallenge.reason ? ` - ${routeChallenge.reason}` : null}
            </p>
          ) : null}
          {message.response?.auditFallbackReason ? (
            <p className="chat-message__auditFallback" data-testid="chat.message.auditFallback">
              {message.response.auditFallbackReason}
            </p>
          ) : null}
          {reasoningSummary ? (
            <p className="chat-message__reasoningSummary" data-testid="chat.message.reasoningSummary">
              {reasoningSummary}
            </p>
          ) : null}
          {reasoningTrace.length > 0 ? (
            <ul>
              {reasoningTrace.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          ) : null}
        </details>
      ) : null}
      {message.role === 'assistant' ? (
        <div className="chat-message__actions" data-testid="chat.message.actions">
          {gate.canCopyMarkdown ? (
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.message.copyMarkdown"
              onClick={() => void navigator.clipboard?.writeText(message.content)}
            >
              Copy Markdown
            </CommandButton>
          ) : null}
          {gate.canSaveDiscussion && onSaveDiscussion ? (
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
          {canViewSources && onViewSources ? (
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

function formatAuditSource(source: ChatAuditSource | undefined) {
  if (source === 'durable') {
    return 'Durable audit';
  }

  if (source === 'tags') {
    return 'Tags replay fallback';
  }

  if (source === 'live') {
    return 'Live response';
  }

  if (source === 'none') {
    return 'No audit metadata';
  }

  return null;
}
