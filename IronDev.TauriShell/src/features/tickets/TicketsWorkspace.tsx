import type { ApiStatus, ProjectTicket } from '../../api/types';
import { AuthRequiredState } from '../../components/AuthRequiredState';
import { ContextInspector } from '../../components/ContextInspector';
import { SurfacePanel } from '../../components/SurfacePanel';
import { TicketDetail } from '../../components/TicketDetail';
import { TicketList } from '../../components/TicketList';
import { WorkspaceLayout } from '../../components/WorkspaceLayout';

interface TicketsWorkspaceProps {
  apiStatus: ApiStatus;
  apiBaseUrl: string;
  projectId: number;
  tokenConfigured: boolean;
  ticketAccessRequiresAuth: boolean;
  authLabel: string;
  tickets: ProjectTicket[];
  selectedTicket: ProjectTicket | null;
  selectedTicketId: number | null;
  ticketMessage: string;
  tokenDraft: string;
  isTokenConfigOpen: boolean;
  onSelectTicket: (ticketId: number) => void;
  onConfigureToken: () => void;
  onRetry: () => void;
  onTokenDraftChange: (value: string) => void;
  onSaveToken: () => void;
}

export function TicketsWorkspace({
  apiStatus,
  apiBaseUrl,
  projectId,
  tokenConfigured,
  ticketAccessRequiresAuth,
  authLabel,
  tickets,
  selectedTicket,
  selectedTicketId,
  ticketMessage,
  tokenDraft,
  isTokenConfigOpen,
  onSelectTicket,
  onConfigureToken,
  onRetry,
  onTokenDraftChange,
  onSaveToken
}: TicketsWorkspaceProps) {
  return (
    <main className="tickets-workspace" data-testid="tickets.workspace">
      <WorkspaceLayout
        left={
          <TicketList
            tickets={tickets}
            selectedTicketId={selectedTicketId}
            message={ticketMessage}
            onSelect={onSelectTicket}
          />
        }
        center={
          ticketAccessRequiresAuth ? (
            <SurfacePanel className="ticket-detail ticket-detail--auth" testId="ticket.detail">
              <AuthRequiredState
                apiStatus={apiStatus}
                authLabel={authLabel}
                tokenDraft={tokenDraft}
                isConfigOpen={isTokenConfigOpen}
                onConfigureToken={onConfigureToken}
                onRetry={onRetry}
                onTokenDraftChange={onTokenDraftChange}
                onSaveToken={onSaveToken}
              />
            </SurfacePanel>
          ) : (
            <TicketDetail ticket={selectedTicket} />
          )
        }
        right={
          <ContextInspector
            ticket={selectedTicket}
            apiBaseUrl={apiBaseUrl}
            projectId={projectId}
            tokenConfigured={tokenConfigured}
          />
        }
      />
    </main>
  );
}
