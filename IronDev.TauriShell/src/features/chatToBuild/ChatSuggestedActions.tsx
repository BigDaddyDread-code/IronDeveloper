import { CommandButton } from '../../components/CommandButton';
import type { ChatModeGate } from './chatGovernanceGate';

interface ChatSuggestedActionsProps {
  hasResponse: boolean;
  responseText: string | null;
  gate: ChatModeGate;
}

export function ChatSuggestedActions({ hasResponse, responseText, gate }: ChatSuggestedActionsProps) {
  const hasKnownMode = Boolean(gate.mode);
  const copyDisabledReason = !responseText ? 'No response to copy yet.' : undefined;

  return (
    <section className="chat-suggested-actions" data-testid="chat.suggestedActions">
      <div className="workflow-section__header">
        <h4>Actions</h4>
      </div>
      {gate.showGovernanceActions && gate.governanceActions.length > 0 ? (
        <ul className="chat-suggested-actions__governance" data-testid="chat.actions.governance">
          {gate.governanceActions.map((action) => (
            <li key={action}>{action}</li>
          ))}
        </ul>
      ) : null}
      <div className="chat-suggested-actions__grid">
        {gate.canCopyMarkdown ? (
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
      {!hasKnownMode && hasResponse ? <p className="state-muted" data-testid="chat.actions.modeHint">Mode is not explicitly set by the backend yet; governance actions are hidden until lane is explicit.</p> : null}
      {gate.mode === 'Exploration' && hasResponse ? <p className="state-muted" data-testid="chat.actions.modeHint">Exploration lane: reasoning is open. Ask for formalization when ready to lock intent.</p> : null}
      {gate.mode === 'Confirmation' && hasResponse ? <p className="state-muted" data-testid="chat.actions.modeHint">Confirmation lane: choose the intended path before governance actions appear.</p> : null}
      {gate.showGovernanceActions && hasResponse ? (
        <p className="state-muted" data-testid="chat.actions.modeHint">
          Formalization lane: governance actions are available.
        </p>
      ) : null}
      {!hasResponse ? <p className="state-muted" data-testid="chat.actions.modeHint">Send a message to unlock response actions.</p> : null}
    </section>
  );
}
