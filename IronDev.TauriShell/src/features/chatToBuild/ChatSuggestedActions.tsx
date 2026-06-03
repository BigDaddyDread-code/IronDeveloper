import { CommandButton } from '../../components/CommandButton';

interface ChatSuggestedActionsProps {
  hasResponse: boolean;
  responseText: string | null;
  governanceActions: string[];
  hasGovernanceActions: boolean;
}

export function ChatSuggestedActions({ hasResponse, responseText, governanceActions, hasGovernanceActions }: ChatSuggestedActionsProps) {
  const copyDisabledReason = !responseText ? 'No response to copy yet.' : undefined;

  return (
    <section className="chat-suggested-actions" data-testid="chat.suggestedActions">
      <div className="workflow-section__header">
        <h4>Actions</h4>
      </div>
      {hasGovernanceActions && governanceActions.length > 0 ? (
        <ul className="chat-suggested-actions__governance" data-testid="chat.actions.governance">
          {governanceActions.map((action) => (
            <li key={action}>{action}</li>
          ))}
        </ul>
      ) : null}
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
          ? hasGovernanceActions
            ? 'Governance actions are available in this response.'
            : 'Exploration response: no governance actions yet. Continue probing or request formalization.'
          : 'Send a message to unlock response actions.'}
      </p>
    </section>
  );
}
