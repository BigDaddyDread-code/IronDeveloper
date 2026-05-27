import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { Surface } from '../../design-system/Surface';
import type { CreateTicketFromDocumentResponse } from '../../api/types';

interface GeneratedTicketPanelProps {
  ticket: CreateTicketFromDocumentResponse | null;
  isBusy: boolean;
  disabledReason: string | null;
  onCreateTicket: () => void;
}

export function GeneratedTicketPanel({ ticket, isBusy, disabledReason, onCreateTicket }: GeneratedTicketPanelProps) {
  return (
    <Surface className="chat-build-panel" testId="chat-build.generatedTicket">
      <div className="section-heading">
        <p className="eyebrow">Ticket</p>
        <h2>Generated ticket</h2>
      </div>
      {ticket ? (
        <div className="metadata-stack">
          <MetadataRow label="Ticket" value={ticket.ticketId} />
          <MetadataRow label="Source document version" value={ticket.sourceDocumentVersionId} />
        </div>
      ) : (
        <p className="state-muted">Create a ticket from the saved discussion document.</p>
      )}
      <div className="chat-build-actions">
        <CommandButton
          type="button"
          variant="secondary"
          onClick={onCreateTicket}
          disabled={isBusy || Boolean(disabledReason)}
          testId="chat-build.command.createTicket"
        >
          {isBusy ? 'Creating...' : 'Create Ticket'}
        </CommandButton>
        {disabledReason ? <p className="state-muted">{disabledReason}</p> : null}
      </div>
    </Surface>
  );
}
