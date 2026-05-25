import type { ProjectTicket } from '../api/types';
import { StatusBadge } from './StatusBadge';

interface TicketListProps {
  tickets: ProjectTicket[];
  selectedTicketId: number | null;
  message: string;
  onSelect: (ticketId: number) => void;
}

export function TicketList({ tickets, selectedTicketId, message, onSelect }: TicketListProps) {
  return (
    <section className="surface-panel ticket-list" data-testid="ticket.list">
      <div className="section-heading">
        <p className="eyebrow">TICKET QUEUE</p>
        <h2>Tickets</h2>
      </div>

      {tickets.length === 0 ? (
        <div className="empty-state">
          <p>{message}</p>
        </div>
      ) : (
        <div className="ticket-list__items">
          {tickets.map((ticket) => (
            <button
              key={ticket.id}
              className={ticket.id === selectedTicketId ? 'ticket-row ticket-row--selected' : 'ticket-row'}
              data-testid="ticket.row"
              onClick={() => onSelect(ticket.id)}
            >
              <span className="ticket-row__rail" aria-hidden="true" />
              <span className="ticket-row__content">
                <span className="ticket-row__title">{ticket.title}</span>
                <span className="ticket-row__summary">{ticket.summary ?? ticket.problem ?? 'No summary captured yet.'}</span>
                <span className="ticket-row__badges">
                  <StatusBadge status="neutral">{ticket.status ?? 'Draft'}</StatusBadge>
                  <StatusBadge status="info">{ticket.priority ?? 'Medium'}</StatusBadge>
                </span>
              </span>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
