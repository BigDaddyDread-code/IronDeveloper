import type { ProjectTicket, TicketEvidenceSummary } from '../api/types';
import { DateTimeDisplay } from '../utils/dateTimeDisplay';
import { EmptyState } from './EmptyState';
import { StatusBadge } from './StatusBadge';
import { WorkspaceListItem } from './WorkspaceListItem';
import { WorkspaceListPane } from './WorkspaceListPane';

interface TicketListProps {
  tickets: ProjectTicket[];
  evidenceSummary?: TicketEvidenceSummary | null;
  selectedTicketId: number | null;
  message: string;
  isLoading?: boolean;
  onSelect: (ticketId: number) => void;
}

export function TicketList({ tickets, evidenceSummary, selectedTicketId, message, isLoading = false, onSelect }: TicketListProps) {
  return (
    <WorkspaceListPane eyebrow="Project work" title="Tickets" testId="ticket.list">
      {tickets.length === 0 ? (
        <EmptyState
          title={isLoading ? 'Loading tickets' : 'No tickets yet'}
          body={isLoading ? message : 'Create a ticket to plan and execute project work.'}
        />
      ) : (
        <div className="workspace-list-pane__items">
          {tickets.map((ticket) => (
            <WorkspaceListItem
              key={ticket.id}
              testId="ticket.row"
              title={ticket.title ?? `Ticket ${ticket.id}`}
              summary={ticket.summary ?? ticket.problem ?? 'No summary captured yet.'}
              isSelected={ticket.id === selectedTicketId}
              onSelect={() => ticket.id !== undefined && onSelect(ticket.id)}
              badges={
                <>
                  <StatusBadge status="neutral">{ticket.status ?? 'Draft'}</StatusBadge>
                  <StatusBadge status="info">{ticket.priority ?? 'Medium'}</StatusBadge>
                  <StatusBadge status="neutral">{ticket.ticketType ?? 'Work item'}</StatusBadge>
                  {ticket.id === selectedTicketId ? (
                    <StatusBadge status={evidenceSummary?.latestRun ? 'ready' : 'neutral'}>
                      {evidenceSummary?.latestRun ? 'Run linked' : 'Readiness pending'}
                    </StatusBadge>
                  ) : null}
                </>
              }
              footer={
                <span className="workspace-list-item__footer">
                  Updated {ticket.createdDate ? DateTimeDisplay.toLocalDisplay(ticket.createdDate) : 'Unavailable'}
                </span>
              }
            />
          ))}
        </div>
      )}
    </WorkspaceListPane>
  );
}
