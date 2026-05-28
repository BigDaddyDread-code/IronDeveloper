import { useEffect, useMemo } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceSummaryChip, WorkspaceRouteMeta } from '../../app/routes';
import { TicketsWorkspace } from './TicketsWorkspace';
import { useTicketsWorkspace } from './useTicketsWorkspace';
import type { TicketsWorkspaceViewModel } from './useTicketsWorkspace';

interface TicketsRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}
export function TicketsRoute({ route, onRouteReady }: TicketsRouteProps) {
  const state = useTicketsWorkspace();

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: 'ticket.startDisposableRun',
        label: 'Start Sandbox Run',
        intent: 'primary',
        onExecute: state.actions.onStartDisposableRun,
        disabled: Boolean(state.startDisposableRunBlockedReason),
        disabledReason: state.startDisposableRunBlockedReason ?? undefined,
        testId: 'ticket.command.startDisposableRun'
      },
      {
        id: 'ticket.reviewLatestRun',
        label: 'Review Run',
        intent: 'secondary',
        onExecute: state.actions.onReviewLatestRun,
        disabled: Boolean(state.reviewLatestRunBlockedReason),
        disabledReason: state.reviewLatestRunBlockedReason ?? undefined,
        testId: 'ticket.command.reviewLatestRun'
      },
      {
        id: 'ticket.refreshReadiness',
        label: state.readinessStatus === 'loading' ? 'Checking...' : 'Refresh Readiness',
        intent: 'secondary',
        onExecute: state.actions.onRefreshReadiness,
        disabled: Boolean(state.editBlockedReason) || state.readinessStatus === 'loading',
        disabledReason: state.editBlockedReason ?? undefined,
        busy: state.readinessStatus === 'loading',
        testId: 'ticket.command.refreshReadiness'
      },
      {
        id: 'ticket.refreshPlan',
        label: state.planStatus === 'loading' ? 'Refreshing Plan...' : 'Refresh Plan',
        intent: 'secondary',
        onExecute: state.actions.onRefreshPlan,
        disabled: Boolean(state.editBlockedReason) || state.planStatus === 'loading',
        disabledReason: state.editBlockedReason ?? undefined,
        busy: state.planStatus === 'loading',
        testId: 'ticket.command.generatePlan'
      },
      {
        id: 'ticket.edit',
        label: 'Edit',
        intent: 'ghost',
        onExecute: state.actions.onEditTicket,
        disabled: Boolean(state.editBlockedReason),
        disabledReason: state.editBlockedReason ?? undefined,
        testId: 'ticket.command.edit'
      },
      {
        id: `workspace.${route.id}.create`,
        label: 'Create Ticket',
        intent: 'secondary',
        onExecute: state.actions.onOpenCreate,
        disabled: Boolean(state.createBlockedReason) || state.isBusy,
        disabledReason: state.createBlockedReason ?? undefined,
        testId: 'ticket.command.create'
      }
    ],
    [
      state.actions,
      state.createBlockedReason,
      state.editBlockedReason,
      state.isBusy,
      state.planStatus,
      state.readinessStatus,
      state.reviewLatestRunBlockedReason,
      state.startDisposableRunBlockedReason
    ]
  );

  const routeSummary: WorkspaceSummaryChip[] = useMemo(
    () => [
      {
        label: `${state.tickets.length} tickets`,
        testId: 'tickets.header'
      }
    ],
    [state.tickets.length]
  );

  const routeState: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: [],
      workspaceBlockReason: state.createBlockedReason,
      workspaceSummaryChips: routeSummary,
      blockReasonTestId: state.createBlockedReason ? 'ticket.create.blockedReason' : undefined
    }),
    [commands, routeSummary, state.createBlockedReason]
  );

  useEffect(() => {
    if (onRouteReady) {
      onRouteReady(routeState);
    }
  }, [onRouteReady, routeState]);

  return <TicketsWorkspace {...mapTicketsPropsFromState(state, commands)} />;
}

function mapTicketsPropsFromState(state: TicketsWorkspaceViewModel, commands: WorkspaceCommand[]) {
  return {
    commands,
    apiStatus: state.apiStatus,
    accessStatus: state.accessStatus,
    apiBaseUrl: state.apiBaseUrl,
    projectId: state.projectId,
    projectStatus: state.projectStatus,
    tokenConfigured: state.tokenConfigured,
    productAccessBlocked: state.projectAccessBlocked,
    authLabel: state.authLabel,
    tenants: state.tenants,
    projects: state.projects,
    selectedTenantId: state.selectedTenantId,
    selectedProjectId: state.selectedProjectId,
    tickets: state.tickets,
    selectedTicket: state.selectedTicket,
    ticketDetailStatus: state.ticketDetailStatus,
    ticketDetailMessage: state.ticketDetailMessage,
    readiness: state.readiness,
    readinessStatus: state.readinessStatus,
    readinessMessage: state.readinessMessage,
    evidenceSummary: state.evidenceSummary,
    evidenceStatus: state.evidenceStatus,
    evidenceMessage: state.evidenceMessage,
    runReview: state.runReview,
    runReviewStatus: state.runReviewStatus,
    runReviewMessage: state.runReviewMessage,
    isRunReviewOpen: state.isRunReviewOpen,
    implementationPlan: state.implementationPlan,
    planStatus: state.planStatus,
    planMessage: state.planMessage,
    isEditingTicket: state.isEditingTicket,
    editDraft: state.editDraft,
    saveStatus: state.saveStatus,
    saveMessage: state.saveMessage,
    isEditDirty: state.isEditDirty,
    editValidationMessage: state.editValidationMessage,
    editBlockedReason: state.editBlockedReason,
    isCreatePanelOpen: state.isCreatePanelOpen,
    createDraft: state.createDraft,
    createStatus: state.createStatus,
    createMessage: state.createMessage,
    createBlockedReason: state.createBlockedReason,
    createdTicketId: state.createdTicketId,
    selectedTicketId: state.selectedTicketId,
    ticketMessage: state.ticketMessage,
    tokenDraft: state.tokenDraft,
    email: state.email,
    password: state.password,
    isTokenConfigOpen: state.isTokenConfigOpen,
    isLocalTestEnvironment: state.isLocalTestEnvironment,
    isBusy: state.isBusy,
    errorMessage: state.errorMessage,
    onSelectTicket: state.actions.onSelectTicket,
    onEditTicket: state.actions.onEditTicket,
    onEditDraftChange: state.actions.onEditDraftChange,
    onSaveTicket: () => state.actions.onSaveTicket(),
    onCancelEditTicket: state.actions.onCancelEditTicket,
    onRefreshPlan: state.actions.onRefreshPlan,
    onRefreshReadiness: state.actions.onRefreshReadiness,
    onRefreshEvidence: state.actions.onRefreshEvidence,
    onReviewLatestRun: state.actions.onReviewLatestRun,
    onRefreshRunReview: state.actions.onRefreshRunReview,
    onDismissRunReview: state.actions.onDismissRunReview,
    onOpenPromotionReview: state.actions.onOpenPromotionReview,
    onCreateDraftChange: state.actions.onCreateDraftChange,
    onSubmitCreateTicket: () => state.actions.onSubmitCreateTicket(),
    onCancelCreateTicket: state.actions.onCancelCreateTicket,
    onConfigureToken: state.actions.onConfigureToken,
    onRetry: state.actions.onRetry,
    onTokenDraftChange: state.actions.onTokenDraftChange,
    onEmailChange: state.actions.onEmailChange,
    onPasswordChange: state.actions.onPasswordChange,
    onSaveToken: state.actions.onSaveToken,
    onSignIn: state.actions.onSignIn,
    onSelectTenant: state.actions.onSelectTenant,
    onSelectProject: state.actions.onSelectProject,
    startDisposableRunBlockedReason: state.startDisposableRunBlockedReason,
    reviewLatestRunBlockedReason: state.reviewLatestRunBlockedReason
  };
}
