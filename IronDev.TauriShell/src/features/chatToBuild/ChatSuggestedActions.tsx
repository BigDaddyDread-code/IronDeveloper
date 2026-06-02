import { CommandButton } from '../../components/CommandButton';

interface ChatSuggestedActionsProps {
  hasResponse: boolean;
  responseText: string | null;
}

export function ChatSuggestedActions({ hasResponse, responseText }: ChatSuggestedActionsProps) {
  const copyDisabledReason = !responseText ? 'No response to copy yet.' : undefined;

  return (
    <section className="chat-suggested-actions" data-testid="chat.suggestedActions">
      <div className="workflow-section__header">
        <h4>Actions</h4>
      </div>
      <div className="chat-suggested-actions__grid">
        <CommandButton
          type="button"
          variant="secondary"
          disabled={!responseText}
          title={copyDisabledReason}
          onClick={() => {
            if (responseText) {
              void navigator.clipboard?.writeText(responseText);
            }
          }}
          testId="chat.action.copy"
        >
          Copy
        </CommandButton>
      </div>
      <p className="state-muted" data-testid="chat.actions.pending">
        {hasResponse
          ? 'Save as document, create ticket, and save decision actions will appear here when those chat actions are wired.'
          : 'Send a message to unlock response actions.'}
      </p>
    </section>
  );
}
