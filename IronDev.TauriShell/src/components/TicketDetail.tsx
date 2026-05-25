import type { ProjectTicket } from '../api/types';
import { EmptyState } from './EmptyState';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';

interface TicketDetailProps {
  ticket: ProjectTicket | null;
  isLoading?: boolean;
}

export function TicketDetail({ ticket, isLoading = false }: TicketDetailProps) {
  if (isLoading) {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <EmptyState title="Loading tickets" body="IronDev is loading the selected project ticket queue." />
      </SurfacePanel>
    );
  }

  if (!ticket) {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <EmptyState
          title="No ticket selected"
          body="Connect to IronDev.Api with a valid token to load the project ticket queue."
        />
      </SurfacePanel>
    );
  }

  return (
    <SurfacePanel className="ticket-detail" testId="ticket.detail">
      <div className="ticket-detail__header">
        <div className="ticket-detail__title">
          <p className="eyebrow">Selected ticket</p>
          <h2>{ticket.title ?? `Ticket ${ticket.id}`}</h2>
          <p>{ticket.summary ?? ticket.problem ?? 'No brief captured yet.'}</p>
        </div>
        <div className="ticket-detail__badges">
          <StatusBadge status="neutral">{ticket.status ?? 'Draft'}</StatusBadge>
          <StatusBadge status="info">{ticket.priority ?? 'Medium'}</StatusBadge>
        </div>
      </div>

      <div className="detail-grid">
        <section className="workflow-section">
          <h3>Brief</h3>
          <p>{ticket.summary ?? ticket.problem ?? 'No brief captured yet.'}</p>
        </section>
        <section className="workflow-section">
          <h3>Readiness</h3>
          <p>{ticket.buildValidation ?? 'Build readiness has not been requested in this spike.'}</p>
        </section>
        <section className="workflow-section">
          <h3>Acceptance</h3>
          <p>{ticket.acceptanceCriteria ?? 'Acceptance criteria unavailable.'}</p>
        </section>
        <section className="workflow-section">
          <h3>Context</h3>
          <p>{ticket.contextSummary ?? 'Context inspector data will remain API-backed in later slices.'}</p>
        </section>
      </div>
    </SurfacePanel>
  );
}
