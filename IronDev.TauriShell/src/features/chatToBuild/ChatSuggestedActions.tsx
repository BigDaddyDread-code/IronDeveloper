import { CommandButton } from '../../components/CommandButton';

interface ChatSuggestedActionsProps {
  hasResponse: boolean;
  responseText: string | null;
  governanceActions: string[];
  hasGovernanceActions: boolean;
  mode: string | null;
}

export function ChatSuggestedActions({ hasResponse, responseText, governanceActions, hasGovernanceActions, mode }: ChatSuggestedActionsProps) {
  const resolvedMode = mode ?? 'Exploration';
  const isExplorationMode = resolvedMode === 'Exploration';
  const copyDisabledReason = !responseText ? 'No response to copy yet.' : undefined;
  const canCopy = !isExplorationMode;

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
        {!isExplorationMode ? (
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
            Copy response
          </CommandButton>
        ) : null}
      </div>
      {!canCopy && hasResponse ? <p className="state-muted" data-testid="chat.actions.modeHint">Exploration lane: reasoning is open. Ask for formalization when ready to lock intent.</p> : null}
      {canCopy && hasResponse ? (
        <p className="state-muted" data-testid="chat.actions.modeHint">
          {hasGovernanceActions ? 'Formalization lane: governance actions are available.' : 'Mode is set to continuation or explicit handoff path.'}
        </p>
      ) : null}
      {!hasResponse ? <p className="state-muted" data-testid="chat.actions.modeHint">Send a message to unlock response actions.</p> : null}
    </section>
  );
}
