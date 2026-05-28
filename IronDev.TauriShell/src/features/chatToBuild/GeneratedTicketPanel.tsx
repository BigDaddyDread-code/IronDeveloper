import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { Surface } from '../../design-system/Surface';
import type { CreateTicketFromDocumentResponse, ProjectTicket } from '../../api/types';

interface GeneratedTicketPanelProps {
  ticket: CreateTicketFromDocumentResponse | null;
  ticketDetail: ProjectTicket | null;
  isBusy: boolean;
  disabledReason: string | null;
  onCreateTicket: () => void;
}

export function GeneratedTicketPanel({
  ticket,
  ticketDetail,
  isBusy,
  disabledReason,
  onCreateTicket
}: GeneratedTicketPanelProps) {
  return (
    <Surface className="chat-build-panel chat-build-panel--ticket" testId="chat-build.generatedTicket">
      <div className="section-heading">
        <p className="eyebrow">Ticket</p>
        <h2>Work contract</h2>
      </div>
      {ticket ? (
        <div className="chat-build-artifact">
          <div className="metadata-stack">
            <MetadataRow label="Ticket" value={ticket.ticketId} />
            <MetadataRow label="Status" value={formatTicketValue(ticketDetail?.status)} />
            <MetadataRow label="Source document version" value={ticket.sourceDocumentVersionId} />
          </div>
          <section className="chat-build-artifact__body">
            <h3>{formatTicketValue(ticketDetail?.title, 'Ticket detail loading')}</h3>
            <p>{formatTicketValue(ticketDetail?.summary ?? ticketDetail?.content, 'The backend created the ticket; detail is not loaded yet.')}</p>
          </section>
          <section className="chat-build-artifact__body">
            <p className="section-subtitle">Problem</p>
            <p>{formatTicketValue(ticketDetail?.problem, 'No problem statement returned by the ticket endpoint.')}</p>
          </section>
          <section className="chat-build-artifact__body">
            <p className="section-subtitle">Acceptance criteria</p>
            {formatLines(ticketDetail?.acceptanceCriteria).length > 0 ? (
              <ul className="chat-build-plain-list">
                {formatLines(ticketDetail?.acceptanceCriteria).map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            ) : (
              <p className="state-muted">No acceptance criteria returned by the ticket endpoint.</p>
            )}
          </section>
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

function formatTicketValue(value: unknown, fallback = 'Unavailable') {
  return typeof value === 'string' && value.trim() ? value : fallback;
}

function formatLines(value: unknown) {
  if (typeof value !== 'string') {
    return [];
  }

  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}
