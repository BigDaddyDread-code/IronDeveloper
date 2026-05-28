import { CommandButton } from '../../components/CommandButton';

interface ChatSuggestedActionsProps {
  hasResponse: boolean;
  responseText: string | null;
}

export function ChatSuggestedActions({ hasResponse, responseText }: ChatSuggestedActionsProps) {
  const copyDisabledReason = !responseText ? 'No response to copy yet.' : undefined;
  const unwiredReason = hasResponse ? 'This action is prepared but not wired to the backend yet.' : 'Send a message first.';

  return (
    <section className="chat-suggested-actions" data-testid="chat.suggestedActions">
      <div className="workflow-section__header">
        <h4>Suggested Actions</h4>
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
        <CommandButton type="button" variant="secondary" disabled title={unwiredReason} testId="chat.action.saveDocument">
          Save as Document
        </CommandButton>
        <CommandButton type="button" variant="secondary" disabled title={unwiredReason} testId="chat.action.createTicket">
          Create Ticket
        </CommandButton>
        <CommandButton type="button" variant="secondary" disabled title={unwiredReason} testId="chat.action.saveDecision">
          Save Decision
        </CommandButton>
        <CommandButton type="button" variant="subtle" disabled title={unwiredReason} testId="chat.action.viewSources">
          View Sources
        </CommandButton>
      </div>
    </section>
  );
}
