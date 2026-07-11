import type {
  WorkspaceCommand
} from '../../app/routes';
import type {
  ApiStatus,
  BuildReadinessResult,
  ProductAccessStatus,
  ProjectImplementationPlan,
  ProjectSummary,
  ProjectTicket,
  TicketEvidenceLoadStatus,
  TicketEvidenceSummary,
  TicketRunReview,
  TicketCreateStatus,
  TenantSummary,
  TicketDetailLoadStatus,
  TicketPlanStatus,
  TicketReadinessLoadStatus,
  TicketSaveStatus
} from '../../api/types';
import { AuthRequiredState } from '../../components/AuthRequiredState';
import { ContextInspector } from '../../components/ContextInspector';
import type { CreateTicketDraft } from '../../components/CreateTicketPanel';
import { CreateTicketPanel } from '../../components/CreateTicketPanel';
import { SurfacePanel } from '../../components/SurfacePanel';
import type { TicketEditDraft } from '../../components/TicketEditForm';
import { TicketDetail } from '../../components/TicketDetail';
import { TicketList } from '../../components/TicketList';
import { TicketRunReviewPanel } from '../../components/TicketRunReviewPanel';
import { StatusBadge } from '../../components/StatusBadge';
import { MetadataGrid } from '../../design-system/metadata/MetadataGrid';
import { WorkspaceFrame } from '../../design-system/workspace/WorkspaceFrame';
import { WorkspaceSplitPane } from '../../design-system/workspace/WorkspaceSplitPane';

interface TicketsWorkspaceProps {
  commands: WorkspaceCommand[];
  apiStatus: ApiStatus;
  accessStatus: ProductAccessStatus;
  apiBaseUrl: string;
  projectId: number | null;
  projectStatus: 'selected' | 'missing' | 'fallback';
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
  evidenceSummary: TicketEvidenceSummary | null;
  evidenceStatus: TicketEvidenceLoadStatus;
  evidenceMessage: string;
  runReview: TicketRunReview | null;
  runReviewStatus: TicketEvidenceLoadStatus;
  runReviewMessage: string;
  isRunReviewOpen: boolean;
  implementationPlan: ProjectImplementationPlan | null;
  planStatus: TicketPlanStatus;
  planMessage: string;
  isEditingTicket: boolean;
  editDraft: TicketEditDraft;
  saveStatus: TicketSaveStatus;
  saveMessage: string;
  isEditDirty: boolean;
  editValidationMessage: string | null;
  editBlockedReason: string | null;
  isCreatePanelOpen: boolean;
  createDraft: CreateTicketDraft;
  createStatus: TicketCreateStatus;
  createMessage: string;
  createBlockedReason: string | null;
  createdTicketId: number | null;
  selectedTicketId: number | null;
  ticketMessage: string;
  tokenDraft: string;
  email: string;
  password: string;
  isTokenConfigOpen: boolean;
  isLocalTestEnvironment: boolean;
  isBusy: boolean;
  errorMessage: string | null;
  onSelectTicket: (ticketId: number) => void;
  onEditTicket: () => void;
  onEditDraftChange: (draft: TicketEditDraft) => void;
  onSaveTicket: () => void;
  onCancelEditTicket: () => void;
  onReloadTicketAndCompare: () => void;
  onRefreshPlan: () => void;
  onRefreshReadiness: () => void;
  onRefreshEvidence: () => void;
  onReviewLatestRun: () => void;
  onRefreshRunReview: () => void;
  onDismissRunReview: () => void;
  onOpenPromotionReview: () => void;
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
  commands,
  apiStatus,
  accessStatus,
  apiBaseUrl,
  projectId,
  projectStatus,
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
  evidenceSummary,
  evidenceStatus,
  evidenceMessage,
  runReview,
  runReviewStatus,
  runReviewMessage,
  isRunReviewOpen,
  implementationPlan,
  planStatus,
  planMessage,
  isEditingTicket,
  editDraft,
  saveStatus,
  saveMessage,
  isEditDirty,
  editValidationMessage,
  editBlockedReason,
  isCreatePanelOpen,
  createDraft,
  createStatus,
  createMessage,
  createBlockedReason,
  createdTicketId,
  selectedTicketId,
  ticketMessage,
  tokenDraft,
  email,
  password,
  isTokenConfigOpen,
  isLocalTestEnvironment,
  isBusy,
  errorMessage,
  onSelectTicket,
  onEditTicket,
  onEditDraftChange,
  onSaveTicket,
  onCancelEditTicket,
  onReloadTicketAndCompare,
  onRefreshPlan,
  onRefreshReadiness,
  onRefreshEvidence,
  onReviewLatestRun,
  onRefreshRunReview,
  onDismissRunReview,
  onOpenPromotionReview,
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
  const projectName = projects.find((project) => project.id === selectedProjectId)?.name ?? null;
  const linkedRun = evidenceSummary?.latestRun ?? null;
  const workspaceMetadata = (
    <MetadataGrid
      items={[
        { label: 'Project', value: projectName ?? (selectedProjectId ? `Project ${selectedProjectId}` : 'Project required') },
        { label: 'Tickets', value: `${tickets.length}` },
        {
          label: 'Selection',
          value: selectedTicket ? `#${selectedTicket.id}` : 'No ticket selected'
        },
        {
          label: 'Run',
          value: linkedRun ? linkedRun.status : 'No reviewed run linked'
        }
      ]}
    />
  );

  return (
    <WorkspaceFrame
      title="Tickets"
      description="Select project work, check build readiness, run safely in a sandbox, and review traceable execution output."
      metadata={workspaceMetadata}
      commands={commands}
      reviewPanel={
        isRunReviewOpen ? (
          <TicketRunReviewPanel
            review={runReview}
            status={runReviewStatus}
            message={runReviewMessage}
            onRefresh={onRefreshRunReview}
            onDismiss={onDismissRunReview}
          />
        ) : null
      }
      testId="tickets.workspace"
    >
      <WorkspaceSplitPane
        list={
          <TicketList
            tickets={tickets}
            evidenceSummary={evidenceSummary}
            selectedTicketId={selectedTicketId}
            message={ticketMessage}
            isLoading={accessStatus === 'loadingTickets' || accessStatus === 'loading'}
            onSelect={onSelectTicket}
          />
        }
        detail={
          isCreatePanelOpen ? (
            <CreateTicketPanel
              projectId={selectedProjectId}
              projectName={projectName}
              projectStatus={projectStatus}
              draft={createDraft}
              status={createStatus}
              message={createMessage}
              blockedReason={createBlockedReason}
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
                isLocalTestEnvironment={isLocalTestEnvironment}
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
            <div className="tickets-workspace__main-stack">
              <div className="tickets-workspace__detail-strip">
                <StatusBadge status={selectedTicket ? 'info' : 'neutral'}>
                  {selectedTicket ? 'Selected ticket detail' : 'No ticket selected'}
                </StatusBadge>
                <StatusBadge status={linkedRun ? 'ready' : 'neutral'}>
                  {linkedRun ? 'Run review linked' : 'No reviewed run linked'}
                </StatusBadge>
              </div>
              <TicketDetail
                ticket={selectedTicket}
                detailStatus={accessStatus === 'loadingTickets' ? 'loading' : ticketDetailStatus}
                detailMessage={ticketDetailMessage}
                readiness={readiness}
                readinessStatus={readinessStatus}
                readinessMessage={readinessMessage}
                evidenceSummary={evidenceSummary}
                evidenceStatus={evidenceStatus}
                evidenceMessage={evidenceMessage}
                implementationPlan={implementationPlan}
                planStatus={planStatus}
                planMessage={planMessage}
                isEditing={isEditingTicket}
                editDraft={editDraft}
                saveStatus={saveStatus}
                saveMessage={saveMessage}
                isEditDirty={isEditDirty}
                editValidationMessage={editValidationMessage}
                editBlockedReason={editBlockedReason}
                onEdit={onEditTicket}
                onEditDraftChange={onEditDraftChange}
                onSave={onSaveTicket}
                onCancelEdit={onCancelEditTicket}
                onReloadTicketAndCompare={onReloadTicketAndCompare}
                onRefreshPlan={onRefreshPlan}
                onRefreshReadiness={onRefreshReadiness}
                onRefreshEvidence={onRefreshEvidence}
                onReviewLatestRun={onReviewLatestRun}
                onOpenPromotionReview={onOpenPromotionReview}
              />
            </div>
          )
        }
        context={
          <ContextInspector
            ticket={selectedTicket}
            evidenceSummary={evidenceSummary}
            evidenceStatus={evidenceStatus}
            evidenceMessage={evidenceMessage}
            readiness={readiness}
            readinessStatus={readinessStatus}
            apiBaseUrl={apiBaseUrl}
            projectId={projectId}
            projectStatus={projectStatus}
            tokenConfigured={tokenConfigured}
            onReviewLatestRun={onReviewLatestRun}
            onOpenPromotionReview={onOpenPromotionReview}
          />
        }
      />
    </WorkspaceFrame>
  );
}
