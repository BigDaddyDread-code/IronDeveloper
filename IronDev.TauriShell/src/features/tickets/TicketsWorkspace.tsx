import type { ApiStatus, ProductAccessStatus, ProjectSummary, ProjectTicket, TenantSummary } from '../../api/types';
import { AuthRequiredState } from '../../components/AuthRequiredState';
import { ContextInspector } from '../../components/ContextInspector';
import { SurfacePanel } from '../../components/SurfacePanel';
import { TicketDetail } from '../../components/TicketDetail';
import { TicketList } from '../../components/TicketList';
import { WorkspaceLayout } from '../../components/WorkspaceLayout';

interface TicketsWorkspaceProps {
  apiStatus: ApiStatus;
  accessStatus: ProductAccessStatus;
  apiBaseUrl: string;
  projectId: number;
  tokenConfigured: boolean;
  productAccessBlocked: boolean;
  authLabel: string;
  tenants: TenantSummary[];
  projects: ProjectSummary[];
  selectedTenantId: number | null;
  selectedProjectId: number | null;
  tickets: ProjectTicket[];
  selectedTicket: ProjectTicket | null;
  selectedTicketId: number | null;
  ticketMessage: string;
  tokenDraft: string;
  email: string;
  password: string;
  isTokenConfigOpen: boolean;
  isBusy: boolean;
  errorMessage: string | null;
  onSelectTicket: (ticketId: number) => void;
  onConfigureToken: () => void;
  onRetry: () => void;
  onTokenDraftChange: (value: string) => void;
  onEmailChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onSaveToken: () => void;
  onSignIn: () => void;
  onSelectTenant: (tenantId: number) => void;
  onSelectProject: (projectId: number) => void;
}

export function TicketsWorkspace({
  apiStatus,
  accessStatus,
  apiBaseUrl,
  projectId,
  tokenConfigured,
  productAccessBlocked,
  authLabel,
  tenants,
  projects,
  selectedTenantId,
  selectedProjectId,
  tickets,
  selectedTicket,
  selectedTicketId,
  ticketMessage,
  tokenDraft,
  email,
  password,
  isTokenConfigOpen,
  isBusy,
  errorMessage,
  onSelectTicket,
  onConfigureToken,
  onRetry,
  onTokenDraftChange,
  onEmailChange,
  onPasswordChange,
  onSaveToken,
  onSignIn,
  onSelectTenant,
  onSelectProject
}: TicketsWorkspaceProps) {
  return (
    <main className="tickets-workspace" data-testid="tickets.workspace">
      <WorkspaceLayout
        left={
          <TicketList
            tickets={tickets}
            selectedTicketId={selectedTicketId}
            message={ticketMessage}
            isLoading={accessStatus === 'loadingTickets' || accessStatus === 'loading'}
            onSelect={onSelectTicket}
          />
        }
        center={
          productAccessBlocked ? (
            <SurfacePanel className="ticket-detail ticket-detail--auth" testId="ticket.detail">
              <AuthRequiredState
                apiStatus={apiStatus}
                accessStatus={accessStatus}
                authLabel={authLabel}
                tokenDraft={tokenDraft}
                email={email}
                password={password}
                isConfigOpen={isTokenConfigOpen}
                tenants={tenants}
                projects={projects}
                selectedTenantId={selectedTenantId}
                selectedProjectId={selectedProjectId}
                isBusy={isBusy}
                errorMessage={errorMessage}
                onConfigureToken={onConfigureToken}
                onRetry={onRetry}
                onTokenDraftChange={onTokenDraftChange}
                onEmailChange={onEmailChange}
                onPasswordChange={onPasswordChange}
                onSaveToken={onSaveToken}
                onSignIn={onSignIn}
                onSelectTenant={onSelectTenant}
                onSelectProject={onSelectProject}
              />
            </SurfacePanel>
          ) : (
            <TicketDetail ticket={selectedTicket} isLoading={accessStatus === 'loadingTickets'} />
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
