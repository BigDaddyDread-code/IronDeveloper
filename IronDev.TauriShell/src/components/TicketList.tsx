import type { ProjectTicket } from '../api/types';
import { EmptyState } from './EmptyState';
import { StatusBadge } from './StatusBadge';
import { WorkspaceListItem } from './WorkspaceListItem';
import { WorkspaceListPane } from './WorkspaceListPane';

interface TicketListProps {
  tickets: ProjectTicket[];
  selectedTicketId: number | null;
  message: string;
  onSelect: (ticketId: number) => void;
}

export function TicketList({ tickets, selectedTicketId, message, onSelect }: TicketListProps) {
  return (
    <WorkspaceListPane eyebrow="Ticket queue" title="Tickets" testId="ticket.list">
      {tickets.length === 0 ? (
        <EmptyState title="No ticket data loaded" body={message} />
      ) : (
        <div className="workspace-list-pane__items">
          {tickets.map((ticket) => (
            <WorkspaceListItem
              key={ticket.id}
              testId="ticket.row"
              title={ticket.title}
              summary={ticket.summary ?? ticket.problem ?? 'No summary captured yet.'}
              isSelected={ticket.id === selectedTicketId}
              onSelect={() => onSelect(ticket.id)}
              badges={
                <>
                  <StatusBadge status="neutral">{ticket.status ?? 'Draft'}</StatusBadge>
                  <StatusBadge status="info">{ticket.priority ?? 'Medium'}</StatusBadge>
                </>
              }
            />
          ))}
        </div>
      )}
    </WorkspaceListPane>
  );
}
