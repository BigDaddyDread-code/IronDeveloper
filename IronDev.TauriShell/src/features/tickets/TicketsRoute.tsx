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

  const commands: WorkspaceCommand[] = [
    {
      id: `workspace.${route.id}.refresh`,
      label: state.isBusy ? 'Refreshing...' : 'Refresh',
      intent: 'secondary',
      onExecute: state.actions.onRetry,
      disabled: state.isBusy,
      busy: state.isBusy,
      testId: route.id === 'tickets' ? 'ticket.command.refresh' : `workspace.${route.id}.refresh`
    },
    {
      id: `workspace.${route.id}.create`,
      label: 'Create Ticket',
      intent: 'primary',
      onExecute: state.actions.onOpenCreate,
      disabled: Boolean(state.createBlockedReason) || state.isBusy,
      disabledReason: state.createBlockedReason,
      testId: 'ticket.command.create'
    }
  ];

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
      workspaceCommands: commands,
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

  return <TicketsWorkspace {...mapTicketsPropsFromState(state)} />;
}

function mapTicketsPropsFromState(state: TicketsWorkspaceViewModel) {
  return {
    apiStatus: state.apiStatus,
    accessStatus: state.accessStatus,
    apiBaseUrl: state.apiBaseUrl,
    projectId: state.projectId,
    projectStatus: state.projectStatus,
    tokenConfigured: state.tokenConfigured,
    projectAccessBlocked: state.projectAccessBlocked,
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
    isBusy: state.isBusy,
    errorMessage: state.errorMessage,
    onSelectTicket: state.actions.onSelectTicket,
    onEditTicket: state.actions.onEditTicket,
    onEditDraftChange: state.actions.onEditDraftChange,
    onSaveTicket: () => state.actions.onSaveTicket(),
    onCancelEditTicket: state.actions.onCancelEditTicket,
    onRefreshPlan: state.actions.onRefreshPlan,
    onRefreshReadiness: state.actions.onRefreshReadiness,
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
    onSelectProject: state.actions.onSelectProject
  };
}
