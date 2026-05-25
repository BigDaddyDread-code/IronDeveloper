import type { ProjectTicket } from '../api/types';
import { StatusBadge } from './StatusBadge';

interface TicketDetailProps {
  ticket: ProjectTicket | null;
}

export function TicketDetail({ ticket }: TicketDetailProps) {
  if (!ticket) {
    return (
      <section className="surface-panel ticket-detail ticket-detail--empty" data-testid="ticket.detail">
        <p className="eyebrow">SELECTED TICKET</p>
        <h2>No ticket selected</h2>
        <p>Connect to IronDev.Api with a valid token to load the project ticket queue.</p>
      </section>
    );
  }

  return (
    <section className="surface-panel ticket-detail" data-testid="ticket.detail">
      <div className="ticket-detail__header">
        <div>
          <p className="eyebrow">SELECTED TICKET</p>
          <h2>{ticket.title}</h2>
        </div>
        <div className="ticket-detail__badges">
          <StatusBadge status="neutral">{ticket.status ?? 'Draft'}</StatusBadge>
          <StatusBadge status="info">{ticket.priority ?? 'Medium'}</StatusBadge>
        </div>
      </div>

      <div className="detail-grid">
        <section>
          <h3>Brief</h3>
          <p>{ticket.summary ?? ticket.problem ?? 'No brief captured yet.'}</p>
        </section>
        <section>
          <h3>Readiness</h3>
          <p>{ticket.buildValidation ?? 'Build readiness has not been requested in this spike.'}</p>
        </section>
        <section>
          <h3>Acceptance</h3>
          <p>{ticket.acceptanceCriteria ?? 'Acceptance criteria unavailable.'}</p>
        </section>
        <section>
          <h3>Context</h3>
          <p>{ticket.contextSummary ?? 'Context inspector data will remain API-backed in later slices.'}</p>
        </section>
      </div>
    </section>
  );
}
