import type {
  ApiStatus,
  BuildReadinessResult,
  ProductAccessStatus,
  ProjectSummary,
  ProjectTicket,
  TicketCreateStatus,
  TenantSummary,
  TicketDetailLoadStatus,
  TicketReadinessLoadStatus
} from '../../api/types';
import { AuthRequiredState } from '../../components/AuthRequiredState';
import { ContextInspector } from '../../components/ContextInspector';
import type { CreateTicketDraft } from '../../components/CreateTicketPanel';
import { CreateTicketPanel } from '../../components/CreateTicketPanel';
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
  ticketDetailStatus: TicketDetailLoadStatus;
  ticketDetailMessage: string;
  readiness: BuildReadinessResult | null;
  readinessStatus: TicketReadinessLoadStatus;
  readinessMessage: string;
  isCreatePanelOpen: boolean;
  createDraft: CreateTicketDraft;
  createStatus: TicketCreateStatus;
  createMessage: string;
  createdTicketId: number | null;
  selectedTicketId: number | null;
  ticketMessage: string;
  tokenDraft: string;
  email: string;
  password: string;
  isTokenConfigOpen: boolean;
  isBusy: boolean;
  errorMessage: string | null;
  onSelectTicket: (ticketId: number) => void;
  onRefreshReadiness: () => void;
  onCreateDraftChange: (draft: CreateTicketDraft) => void;
  onSubmitCreateTicket: () => void;
  onCancelCreateTicket: () => void;
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
  ticketDetailStatus,
  ticketDetailMessage,
  readiness,
  readinessStatus,
  readinessMessage,
  isCreatePanelOpen,
  createDraft,
  createStatus,
  createMessage,
  createdTicketId,
  selectedTicketId,
  ticketMessage,
  tokenDraft,
  email,
  password,
  isTokenConfigOpen,
  isBusy,
  errorMessage,
  onSelectTicket,
  onRefreshReadiness,
  onCreateDraftChange,
  onSubmitCreateTicket,
  onCancelCreateTicket,
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
          isCreatePanelOpen ? (
            <CreateTicketPanel
              projectId={selectedProjectId}
              projectName={projects.find((project) => project.id === selectedProjectId)?.name ?? null}
              draft={createDraft}
              status={createStatus}
              message={createMessage}
              createdTicketId={createdTicketId}
              onChange={onCreateDraftChange}
              onSubmit={onSubmitCreateTicket}
              onCancel={onCancelCreateTicket}
            />
          ) : productAccessBlocked ? (
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
            <TicketDetail
              ticket={selectedTicket}
              detailStatus={accessStatus === 'loadingTickets' ? 'loading' : ticketDetailStatus}
              detailMessage={ticketDetailMessage}
              readiness={readiness}
              readinessStatus={readinessStatus}
              readinessMessage={readinessMessage}
              onRefreshReadiness={onRefreshReadiness}
            />
          )
        }
        right={
          <ContextInspector
            ticket={selectedTicket}
            readiness={readiness}
            readinessStatus={readinessStatus}
            apiBaseUrl={apiBaseUrl}
            projectId={projectId}
            tokenConfigured={tokenConfigured}
          />
        }
      />
    </main>
  );
}
