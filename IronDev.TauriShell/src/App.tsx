import { useCallback, useEffect, useMemo, useState } from 'react';
import { createIronDevApiClient, getIronDevApiConfig, IronDevApiError } from './api/ironDevApi';
import type {
  ApiStatus,
  BuildReadinessResult,
  CreateProjectTicketRequest,
  ProductAccessStatus,
  ProjectImplementationPlan,
  ProjectSummary,
  ProjectTicket,
  TicketCreateStatus,
  TicketDetailLoadStatus,
  TicketPlanStatus,
  TicketReadinessLoadStatus,
  TicketSaveStatus,
  TenantSummary,
  UserProfile
} from './api/types';
import { ApiStatusBadge } from './components/ApiStatusBadge';
import { AppShell } from './components/AppShell';
import type { CreateTicketDraft } from './components/CreateTicketPanel';
import { StatusBadge } from './components/StatusBadge';
import type { TicketEditDraft } from './components/TicketEditForm';
import { WorkspaceHeader } from './components/WorkspaceHeader';
import { TicketsWorkspace } from './features/tickets/TicketsWorkspace';

const initialConfig = getIronDevApiConfig();

const initialStatus: ApiStatus = {
  status: 'loading',
  baseUrl: initialConfig.apiBaseUrl,
  message: 'Checking IronDev.Api...'
};

const initialCreateDraft: CreateTicketDraft = {
  title: '',
  summary: '',
  type: 'Feature / Workflow',
  priority: 'Medium',
  acceptanceCriteria: ''
};

const initialEditDraft: TicketEditDraft = {
  title: '',
  summary: '',
  problem: '',
  proposedChange: '',
  type: '',
  priority: 'Medium',
  acceptanceCriteria: '',
  technicalNotes: '',
  unitTests: '',
  integrationTests: '',
  manualTests: '',
  regressionTests: '',
  buildValidation: ''
};

export default function App() {
  const [configVersion, setConfigVersion] = useState(0);
  const config = useMemo(() => getIronDevApiConfig(), [configVersion]);
  const client = useMemo(() => createIronDevApiClient(config), [config]);

  const [apiStatus, setApiStatus] = useState<ApiStatus>(initialStatus);
  const [accessStatus, setAccessStatus] = useState<ProductAccessStatus>('loading');
  const [userProfile, setUserProfile] = useState<UserProfile | null>(null);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [selectedTicketId, setSelectedTicketId] = useState<number | null>(null);
  const [selectedTicketDetail, setSelectedTicketDetail] = useState<ProjectTicket | null>(null);
  const [ticketDetailStatus, setTicketDetailStatus] = useState<TicketDetailLoadStatus>('idle');
  const [ticketDetailMessage, setTicketDetailMessage] = useState('Select a ticket to load detail.');
  const [readiness, setReadiness] = useState<BuildReadinessResult | null>(null);
  const [readinessStatus, setReadinessStatus] = useState<TicketReadinessLoadStatus>('idle');
  const [readinessMessage, setReadinessMessage] = useState('Build readiness has not been checked for this ticket.');
  const [implementationPlan, setImplementationPlan] = useState<ProjectImplementationPlan | null>(null);
  const [planStatus, setPlanStatus] = useState<TicketPlanStatus>('idle');
  const [planMessage, setPlanMessage] = useState('Plan has not been refreshed for this ticket.');
  const [selectedTenantId, setSelectedTenantId] = useState<number | null>(config.selectedTenantId ?? null);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(config.selectedProjectId ?? null);
  const [selectedProjectName, setSelectedProjectName] = useState<string | null>(null);
  const [projectSelectionMode, setProjectSelectionMode] = useState<'api' | 'fallback-config' | 'missing'>(
    config.selectedProjectId ? 'api' : 'missing'
  );
  const [ticketMessage, setTicketMessage] = useState('Waiting for API health check.');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isTokenConfigOpen, setIsTokenConfigOpen] = useState(false);
  const [tokenDraft, setTokenDraft] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isCreatePanelOpen, setIsCreatePanelOpen] = useState(false);
  const [createDraft, setCreateDraft] = useState<CreateTicketDraft>(initialCreateDraft);
  const [createStatus, setCreateStatus] = useState<TicketCreateStatus>('idle');
  const [createMessage, setCreateMessage] = useState('Create a new IronDev ticket in the selected project.');
  const [createdTicketId, setCreatedTicketId] = useState<number | null>(null);
  const [isEditingTicket, setIsEditingTicket] = useState(false);
  const [editDraft, setEditDraft] = useState<TicketEditDraft>(initialEditDraft);
  const [saveStatus, setSaveStatus] = useState<TicketSaveStatus>('idle');
  const [saveMessage, setSaveMessage] = useState('Ticket is clean.');

  const tokenConfigured = Boolean(config.token);
  const selectedTicketFromQueue = tickets.find((ticket) => ticket.id === selectedTicketId) ?? tickets[0] ?? null;
  const selectedTicket = selectedTicketDetail?.id === selectedTicketId ? selectedTicketDetail : selectedTicketFromQueue;
  const selectedTicketIdForList = selectedTicket?.id ?? null;
  const productAccessBlocked = !['ready', 'emptyTickets', 'loadingTickets'].includes(accessStatus);
  const projectBadgeStatus = selectedProjectId ? (projectSelectionMode === 'fallback-config' ? 'fallback' : 'selected') : 'missing';
  const createBlockedReason = isEditingTicket
    ? 'Save or cancel the current ticket changes before creating another ticket.'
    : getCreateTicketBlocker(apiStatus, accessStatus, tokenConfigured, selectedTenantId, selectedProjectId, projectSelectionMode);
  const ticketActionBlockedReason = getTicketActionBlocker(
    apiStatus,
    accessStatus,
    tokenConfigured,
    selectedTenantId,
    selectedProjectId,
    projectSelectionMode,
    selectedTicketIdForList
  );
  const editValidationMessage = validateEditDraft(editDraft);
  const isEditDirty = selectedTicket ? !areEditDraftsEqual(editDraft, draftFromTicket(selectedTicket)) : false;

  const refreshConfig = useCallback(() => {
    setConfigVersion((value) => value + 1);
  }, []);

  const openCreatePanel = useCallback(() => {
    setIsCreatePanelOpen(true);
    setIsEditingTicket(false);
    setCreatedTicketId(null);

    const blocker = getCreateTicketBlocker(
      apiStatus,
      accessStatus,
      tokenConfigured,
      selectedTenantId,
      selectedProjectId,
      projectSelectionMode
    );

    if (blocker) {
      setCreateStatus('error');
      setCreateMessage(blocker);
    } else {
      setCreateStatus('idle');
      setCreateMessage('Create a new IronDev ticket in the selected project.');
    }
  }, [accessStatus, apiStatus, projectSelectionMode, selectedProjectId, selectedTenantId, tokenConfigured]);

  const closeCreatePanel = useCallback(() => {
    setIsCreatePanelOpen(false);
    setCreateStatus('idle');
    setCreateMessage('Create a new IronDev ticket in the selected project.');
    setCreatedTicketId(null);
  }, []);

  const resetTicketWorkflowState = useCallback(() => {
    setImplementationPlan(null);
    setPlanStatus('idle');
    setPlanMessage('Plan has not been refreshed for this ticket.');
    setIsEditingTicket(false);
    setEditDraft(initialEditDraft);
    setSaveStatus('idle');
    setSaveMessage('Ticket is clean.');
  }, []);

  const refresh = useCallback(async () => {
    const controller = new AbortController();
    setIsRefreshing(true);
    setErrorMessage(null);
    setApiStatus({ status: 'loading', baseUrl: config.apiBaseUrl, message: 'Checking IronDev.Api...' });
    setAccessStatus('loading');

    const health = await client.checkHealth(controller.signal);
    setApiStatus(health);

    if (health.status !== 'connected') {
      setUserProfile(null);
      setTenants([]);
      setProjects([]);
      setSelectedTenantId(null);
      setSelectedProjectId(null);
      setSelectedProjectName(null);
      setProjectSelectionMode('missing');
      setTickets([]);
      setSelectedTicketId(null);
      setSelectedTicketDetail(null);
      setReadiness(null);
      setReadinessStatus('idle');
      resetTicketWorkflowState();
      setAccessStatus(health.status === 'disconnected' ? 'apiOffline' : 'apiError');
      setTicketMessage(health.message);
      setIsRefreshing(false);
      return;
    }

    if (!config.token) {
      setUserProfile(null);
      setTenants([]);
      setProjects([]);
      setSelectedTenantId(null);
      setSelectedProjectId(null);
      setSelectedProjectName(null);
      setProjectSelectionMode('missing');
      setTickets([]);
      setSelectedTicketId(null);
      setSelectedTicketDetail(null);
      setReadiness(null);
      setReadinessStatus('idle');
      resetTicketWorkflowState();
      setAccessStatus('authRequired');
      setTicketMessage('Sign in or configure a token to load tickets.');
      setIsRefreshing(false);
      return;
    }

    try {
      const profile = await client.getCurrentUser(controller.signal);
      setUserProfile(profile);

      const tenantList = await client.getTenants(controller.signal);
      setTenants(tenantList);

      const tenantId = profile.selectedTenantId ?? config.selectedTenantId ?? null;
      setSelectedTenantId(tenantId);

      if (!tenantId) {
        setProjects([]);
        setSelectedProjectId(null);
        setSelectedProjectName(null);
        setProjectSelectionMode('missing');
        setTickets([]);
        setSelectedTicketId(null);
        setSelectedTicketDetail(null);
        setReadiness(null);
        setReadinessStatus('idle');
        resetTicketWorkflowState();
        setAccessStatus('tenantRequired');
        setTicketMessage('Select a tenant before loading project tickets.');
        setIsRefreshing(false);
        return;
      }

      const projectList = await client.getProjects(controller.signal);
      setProjects(projectList);

      const selectedProject = selectProject(projectList, config.selectedProjectId, config.fallbackProjectId);
      const projectId = selectedProject?.id ?? null;
      setSelectedProjectId(projectId ?? null);
      setSelectedProjectName(selectedProject?.name ?? null);
      setProjectSelectionMode(selectedProject?.mode ?? 'missing');

      if (!projectId) {
        setTickets([]);
        setSelectedTicketId(null);
        setSelectedTicketDetail(null);
        setReadiness(null);
        setReadinessStatus('idle');
        resetTicketWorkflowState();
        setAccessStatus('projectRequired');
        setTicketMessage('Select a project before loading tickets.');
        setIsRefreshing(false);
        return;
      }

      if (selectedProject?.mode === 'api') {
        await client.selectProject(projectId, controller.signal).catch(() => undefined);
      }

      setAccessStatus('loadingTickets');
      setTicketMessage('Loading tickets...');
      const ticketResult = await client.getProjectTickets(projectId, controller.signal);
      setTickets(ticketResult.tickets);
      setSelectedTicketId(ticketResult.tickets[0]?.id ?? null);
      setSelectedTicketDetail(null);
      setReadiness(null);
      setReadinessStatus('idle');
      resetTicketWorkflowState();
      setTicketMessage(ticketResult.message);
      setAccessStatus(ticketResult.tickets.length === 0 ? 'emptyTickets' : 'ready');
    } catch (error) {
      setSelectedProjectId(null);
      setSelectedProjectName(null);
      setProjectSelectionMode('missing');
      setTickets([]);
      setSelectedTicketId(null);
      setSelectedTicketDetail(null);
      setReadiness(null);
      setReadinessStatus('idle');
      resetTicketWorkflowState();

      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setAccessStatus('authInvalid');
        setTicketMessage('IronDev.Api rejected the current token.');
      } else if (error instanceof IronDevApiError) {
        setAccessStatus('apiError');
        setTicketMessage(`IronDev.Api request failed with HTTP ${error.status}.`);
      } else {
        setAccessStatus('apiOffline');
        setTicketMessage('IronDev.Api request failed.');
      }
    } finally {
      setIsRefreshing(false);
    }
  }, [client, config, resetTicketWorkflowState]);

  const saveToken = useCallback(() => {
    const trimmed = tokenDraft.trim();

    if (!trimmed) {
      return;
    }

    window.localStorage.setItem('irondev.token', trimmed);
    setTokenDraft('');
    setIsTokenConfigOpen(false);
    refreshConfig();
  }, [refreshConfig, tokenDraft]);

  const refreshReadiness = useCallback(async () => {
    if (!selectedProjectId || !selectedTicketId) {
      setReadiness(null);
      setReadinessStatus('unavailable');
      setReadinessMessage('Select a project ticket before checking build readiness.');
      return;
    }

    setReadinessStatus('loading');
    setReadinessMessage('Checking build readiness through IronDev.Api...');

    try {
      const result = await client.getTicketBuildReadiness(selectedProjectId, selectedTicketId);
      setReadiness(result);
      setReadinessStatus('loaded');
      setReadinessMessage(result.message ?? 'Build readiness returned without a message.');
    } catch (error) {
      setReadiness(null);

      if (error instanceof IronDevApiError && error.status === 404) {
        setReadinessStatus('unavailable');
        setReadinessMessage('Build readiness is not available for this ticket yet.');
      } else if (error instanceof IronDevApiError) {
        setReadinessStatus('error');
        setReadinessMessage(`Build readiness failed with HTTP ${error.status}.`);
      } else {
        setReadinessStatus('error');
        setReadinessMessage('Build readiness request could not reach IronDev.Api.');
      }
    }
  }, [client, selectedProjectId, selectedTicketId]);

  const createTicket = useCallback(async () => {
    setCreateStatus('validating');
    setCreatedTicketId(null);

    const blocker = getCreateTicketBlocker(
      apiStatus,
      accessStatus,
      tokenConfigured,
      selectedTenantId,
      selectedProjectId,
      projectSelectionMode
    );

    if (blocker) {
      setCreateStatus('error');
      setCreateMessage(blocker);
      return;
    }

    const title = createDraft.title.trim();
    const summary = createDraft.summary.trim();

    if (!title) {
      setCreateStatus('error');
      setCreateMessage('Title is required before IronDev can create a ticket.');
      return;
    }

    if (!summary) {
      setCreateStatus('error');
      setCreateMessage('Summary is required so the ticket has enough context.');
      return;
    }

    const projectId = selectedProjectId;

    if (!projectId) {
      setCreateStatus('error');
      setCreateMessage('Select a project before creating a ticket.');
      return;
    }

    const request: CreateProjectTicketRequest = {
      title,
      summary,
      type: createDraft.type.trim() || undefined,
      priority: createDraft.priority.trim() || undefined,
      acceptanceCriteria: splitAcceptanceCriteria(createDraft.acceptanceCriteria),
      provenance: {
        source: 'tauri-shell',
        createdBy: userProfile?.displayName ?? 'tauri-shell',
        notes: 'Created from the Tauri Tickets cockpit.'
      }
    };

    setCreateStatus('submitting');
    setCreateMessage('Creating ticket through IronDev.Api...');

    try {
      const createdTicket = await client.createProjectTicket(projectId, request);
      const ticketResult = await client.getProjectTickets(projectId);
      const createdId = createdTicket.id ?? null;

      setTickets(ticketResult.tickets);
      setSelectedTicketId(createdId ?? ticketResult.tickets[0]?.id ?? null);
      setSelectedTicketDetail(createdTicket);
      setTicketDetailStatus('loaded');
      setTicketDetailMessage('Created ticket detail loaded.');
      setReadiness(null);
      setReadinessStatus('idle');
      setReadinessMessage('Build readiness has not been checked for this ticket.');
      setTicketMessage(`Loaded ${ticketResult.tickets.length} ticket(s) after create.`);
      setAccessStatus(ticketResult.tickets.length === 0 ? 'emptyTickets' : 'ready');
      setImplementationPlan(null);
      setPlanStatus('idle');
      setPlanMessage('Plan has not been refreshed for this ticket.');
      setIsEditingTicket(false);
      setEditDraft(draftFromTicket(createdTicket));
      setSaveStatus('idle');
      setSaveMessage('Ticket is clean.');
      setCreatedTicketId(createdId);
      setCreateStatus('success');
      setCreateMessage(createdId ? `IronDev ticket #${createdId} was created and selected.` : 'IronDev ticket was created.');
      setCreateDraft(initialCreateDraft);
    } catch (error) {
      setCreateStatus('error');

      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setCreateMessage('IronDev.Api rejected the current token. Sign in again before creating tickets.');
      } else if (error instanceof IronDevApiError && error.status === 400) {
        setCreateMessage('IronDev.Api rejected the ticket payload. Check the title, summary, and acceptance criteria.');
      } else if (error instanceof IronDevApiError) {
        setCreateMessage(`Ticket creation failed with HTTP ${error.status}.`);
      } else {
        setCreateMessage('Ticket creation could not reach IronDev.Api.');
      }
    }
  }, [
    accessStatus,
    apiStatus,
    client,
    createDraft,
    selectedProjectId,
    selectedTenantId,
    projectSelectionMode,
    tokenConfigured,
    userProfile?.displayName
  ]);

  const handleSelectTicket = useCallback(
    (ticketId: number) => {
      if (isEditingTicket && isEditDirty) {
        setSaveStatus('validation');
        setSaveMessage('Save or cancel the current ticket changes before switching selection.');
        return;
      }

      setSelectedTicketId(ticketId);
      setIsCreatePanelOpen(false);
      setIsEditingTicket(false);
      setSaveStatus('idle');
      setSaveMessage('Ticket is clean.');
    },
    [isEditDirty, isEditingTicket]
  );

  const beginEditTicket = useCallback(() => {
    if (!selectedTicket) {
      setSaveStatus('validation');
      setSaveMessage('Select a ticket before editing.');
      return;
    }

    if (ticketActionBlockedReason) {
      setSaveStatus('validation');
      setSaveMessage(ticketActionBlockedReason);
      return;
    }

    setIsCreatePanelOpen(false);
    setEditDraft(draftFromTicket(selectedTicket));
    setSaveStatus('editing');
    setSaveMessage('Editing selected ticket.');
    setIsEditingTicket(true);
  }, [selectedTicket, ticketActionBlockedReason]);

  const cancelEditTicket = useCallback(() => {
    setEditDraft(selectedTicket ? draftFromTicket(selectedTicket) : initialEditDraft);
    setSaveStatus('idle');
    setSaveMessage('Ticket changes discarded.');
    setIsEditingTicket(false);
  }, [selectedTicket]);

  const saveTicket = useCallback(async () => {
    if (!selectedTicket || !selectedTicket.id) {
      setSaveStatus('validation');
      setSaveMessage('Select a ticket before saving.');
      return;
    }

    if (ticketActionBlockedReason) {
      setSaveStatus('validation');
      setSaveMessage(ticketActionBlockedReason);
      return;
    }

    const validationMessage = validateEditDraft(editDraft);

    if (validationMessage) {
      setSaveStatus('validation');
      setSaveMessage(validationMessage);
      return;
    }

    if (!isEditDirty) {
      setSaveStatus('idle');
      setSaveMessage('No changes to save.');
      return;
    }

    const projectId = selectedProjectId;

    if (!projectId) {
      setSaveStatus('validation');
      setSaveMessage('Select a project before saving.');
      return;
    }

    setSaveStatus('saving');
    setSaveMessage('Saving ticket through IronDev.Api...');

    try {
      const savedTicket = await client.saveProjectTicket(projectId, buildTicketFromEditDraft(selectedTicket, editDraft, projectId));
      setTickets((current) =>
        current.map((ticket) => (ticket.id === savedTicket.id ? { ...ticket, ...savedTicket } : ticket))
      );
      setSelectedTicketId(savedTicket.id ?? selectedTicket.id ?? null);
      setSelectedTicketDetail(savedTicket);
      setEditDraft(draftFromTicket(savedTicket));
      setIsEditingTicket(false);
      setSaveStatus('saved');
      setSaveMessage('Ticket saved through IronDev.Api.');
      setTicketMessage('Ticket saved and local queue state refreshed.');
    } catch (error) {
      setSaveStatus('error');

      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setSaveMessage('IronDev.Api rejected the current token. Sign in again before saving tickets.');
      } else if (error instanceof IronDevApiError && error.status === 400) {
        setSaveMessage('IronDev.Api rejected the ticket update. Check required fields.');
      } else if (error instanceof IronDevApiError) {
        setSaveMessage(`Ticket save failed with HTTP ${error.status}.`);
      } else {
        setSaveMessage('Ticket save could not reach IronDev.Api.');
      }
    }
  }, [client, editDraft, isEditDirty, selectedProjectId, selectedTicket, ticketActionBlockedReason]);

  const refreshImplementationPlan = useCallback(async () => {
    if (!selectedTicketIdForList) {
      setPlanStatus('unavailable');
      setPlanMessage('Select a ticket before refreshing the implementation plan.');
      return;
    }

    if (ticketActionBlockedReason) {
      setPlanStatus('unavailable');
      setPlanMessage(ticketActionBlockedReason);
      return;
    }

    setPlanStatus('loading');
    setPlanMessage('Refreshing implementation plan through IronDev.Api...');

    try {
      const plan = await client.getTicketImplementationPlan(selectedTicketIdForList);
      setImplementationPlan(plan);
      setPlanStatus('loaded');
      setPlanMessage(plan.proposedSteps || plan.goal ? 'Implementation plan loaded.' : 'Implementation plan returned without detailed steps.');
    } catch (error) {
      setImplementationPlan(null);

      if (error instanceof IronDevApiError && error.status === 404) {
        setPlanStatus('unavailable');
        setPlanMessage('Plan not available yet. The API has not exposed a plan for this ticket.');
      } else if (error instanceof IronDevApiError) {
        setPlanStatus('error');
        setPlanMessage(`Plan refresh failed with HTTP ${error.status}.`);
      } else {
        setPlanStatus('error');
        setPlanMessage('Plan refresh could not reach IronDev.Api.');
      }
    }
  }, [client, selectedTicketIdForList, ticketActionBlockedReason]);

  const signIn = useCallback(async () => {
    if (!email.trim() || !password) {
      setErrorMessage('Email and password are required.');
      return;
    }

    setIsRefreshing(true);
    setErrorMessage(null);

    try {
      const response = await client.login({ email: email.trim(), password });
      window.localStorage.setItem('irondev.token', response.token);
      window.localStorage.removeItem('irondev.tenantId');
      refreshConfig();
    } catch (error) {
      setErrorMessage(error instanceof IronDevApiError ? 'Sign in failed. Check credentials and retry.' : 'Sign in failed.');
      setAccessStatus('authInvalid');
    } finally {
      setIsRefreshing(false);
    }
  }, [client, email, password, refreshConfig]);

  const selectTenantContext = useCallback(
    async (tenantId: number) => {
      if (!Number.isFinite(tenantId)) {
        return;
      }

      setIsRefreshing(true);
      setErrorMessage(null);

      try {
        const response = await client.selectTenant(tenantId);
        window.localStorage.setItem('irondev.token', response.token);
        window.localStorage.setItem('irondev.tenantId', `${tenantId}`);
        window.localStorage.removeItem('irondev.selectedProjectId');
        setSelectedTenantId(tenantId);
        refreshConfig();
      } catch {
        setErrorMessage('Tenant selection failed. Confirm the token has access to this tenant.');
      } finally {
        setIsRefreshing(false);
      }
    },
    [client, refreshConfig]
  );

  const selectProjectContext = useCallback(
    (projectId: number) => {
      if (!Number.isFinite(projectId)) {
        return;
      }

      window.localStorage.setItem('irondev.selectedProjectId', `${projectId}`);
      setSelectedProjectId(projectId);
      setSelectedProjectName(projects.find((project) => project.id === projectId)?.name ?? `Project ${projectId}`);
      setProjectSelectionMode('api');
      refreshConfig();
    },
    [projects, refreshConfig]
  );

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (productAccessBlocked || !selectedProjectId || !selectedTicketId) {
      setSelectedTicketDetail(null);
      setTicketDetailStatus('idle');
      setTicketDetailMessage('Select a ticket to load detail.');
      setReadiness(null);
      setReadinessStatus('idle');
      setReadinessMessage('Build readiness has not been checked for this ticket.');
      setImplementationPlan(null);
      setPlanStatus('idle');
      setPlanMessage('Plan has not been refreshed for this ticket.');
      setIsEditingTicket(false);
      setEditDraft(initialEditDraft);
      setSaveStatus('idle');
      setSaveMessage('Ticket is clean.');
      return;
    }

    const controller = new AbortController();
    setTicketDetailStatus('loading');
    setTicketDetailMessage('Loading selected ticket detail through IronDev.Api...');
    setReadiness(null);
    setReadinessStatus('idle');
    setReadinessMessage('Build readiness has not been checked for this ticket.');
    setImplementationPlan(null);
    setPlanStatus('idle');
    setPlanMessage('Plan has not been refreshed for this ticket.');
    setIsEditingTicket(false);
    setSaveStatus('idle');
    setSaveMessage('Ticket is clean.');

    client
      .getProjectTicket(selectedProjectId, selectedTicketId, controller.signal)
      .then((ticket) => {
        if (controller.signal.aborted) {
          return;
        }

        setSelectedTicketDetail(ticket);
        setEditDraft(draftFromTicket(ticket));
        setTicketDetailStatus('loaded');
        setTicketDetailMessage('Ticket detail loaded.');
      })
      .catch((error) => {
        if (controller.signal.aborted) {
          return;
        }

        setSelectedTicketDetail(null);
        setTicketDetailStatus('error');
        setTicketDetailMessage(
          error instanceof IronDevApiError
            ? `Ticket detail failed with HTTP ${error.status}.`
            : 'Ticket detail request could not reach IronDev.Api.'
        );
      });

    return () => controller.abort();
  }, [client, productAccessBlocked, selectedProjectId, selectedTicketId]);

  return (
    <AppShell
      header={
        <WorkspaceHeader
          apiStatus={apiStatus}
          projectId={selectedProjectId}
          projectName={selectedProjectName}
          projectStatus={projectBadgeStatus}
          ticketCount={tickets.length}
          tokenConfigured={tokenConfigured}
          userDisplayName={userProfile?.displayName ?? null}
          tenantName={tenants.find((tenant) => tenant.id === selectedTenantId)?.name ?? null}
          isRefreshing={isRefreshing}
          createBlockedReason={createBlockedReason}
          onRefresh={() => void refresh()}
          onCreateTicket={openCreatePanel}
        />
      }
      navigation={
        <nav className="shell-nav" aria-label="Workspace navigation">
          <button className="shell-nav__item shell-nav__item--active" data-testid="shell.nav.tickets">
            Tickets
          </button>
        </nav>
      }
      footer={
        <footer className="shell-footer">
          <ApiStatusBadge status={apiStatus.status} withTestId={false} />
          <span>{apiStatus.message}</span>
          {productAccessBlocked ? <StatusBadge status="authRequired">{statusLabel(accessStatus)}</StatusBadge> : null}
        </footer>
      }
    >
      <TicketsWorkspace
        apiStatus={apiStatus}
        accessStatus={accessStatus}
        apiBaseUrl={config.apiBaseUrl}
        projectId={selectedProjectId}
        projectStatus={projectBadgeStatus}
        tokenConfigured={tokenConfigured}
        productAccessBlocked={productAccessBlocked}
        authLabel={tokenConfigured ? 'Token rejected' : 'Missing token'}
        tenants={tenants}
        projects={projects}
        selectedTenantId={selectedTenantId}
        selectedProjectId={selectedProjectId}
        tickets={tickets}
        selectedTicket={selectedTicket}
        ticketDetailStatus={ticketDetailStatus}
        ticketDetailMessage={ticketDetailMessage}
        readiness={readiness}
        readinessStatus={readinessStatus}
        readinessMessage={readinessMessage}
        implementationPlan={implementationPlan}
        planStatus={planStatus}
        planMessage={planMessage}
        isEditingTicket={isEditingTicket}
        editDraft={editDraft}
        saveStatus={saveStatus}
        saveMessage={saveMessage}
        isEditDirty={isEditDirty}
        editValidationMessage={editValidationMessage}
        editBlockedReason={ticketActionBlockedReason}
        isCreatePanelOpen={isCreatePanelOpen}
        createDraft={createDraft}
        createStatus={createStatus}
        createMessage={createMessage}
        createBlockedReason={createBlockedReason}
        createdTicketId={createdTicketId}
        selectedTicketId={selectedTicketIdForList}
        ticketMessage={ticketMessage}
        tokenDraft={tokenDraft}
        email={email}
        password={password}
        isTokenConfigOpen={isTokenConfigOpen}
        isBusy={isRefreshing}
        errorMessage={errorMessage}
        onSelectTicket={handleSelectTicket}
        onEditTicket={beginEditTicket}
        onEditDraftChange={setEditDraft}
        onSaveTicket={() => void saveTicket()}
        onCancelEditTicket={cancelEditTicket}
        onRefreshPlan={() => void refreshImplementationPlan()}
        onRefreshReadiness={() => void refreshReadiness()}
        onCreateDraftChange={setCreateDraft}
        onSubmitCreateTicket={() => void createTicket()}
        onCancelCreateTicket={closeCreatePanel}
        onConfigureToken={() => setIsTokenConfigOpen((value) => !value)}
        onRetry={() => void refresh()}
        onTokenDraftChange={setTokenDraft}
        onEmailChange={setEmail}
        onPasswordChange={setPassword}
        onSaveToken={saveToken}
        onSignIn={() => void signIn()}
        onSelectTenant={(tenantId) => void selectTenantContext(tenantId)}
        onSelectProject={selectProjectContext}
      />
    </AppShell>
  );
}

function selectProject(projects: ProjectSummary[], selectedProjectId?: number, fallbackProjectId?: number) {
  const selectedProject = projects.find((project) => project.id === selectedProjectId);

  if (selectedProject) {
    return { ...selectedProject, mode: 'api' as const };
  }

  if (!selectedProjectId) {
    const fallbackProject = projects.find((project) => project.id === fallbackProjectId);

    if (fallbackProject) {
      return { ...fallbackProject, mode: 'fallback-config' as const };
    }
  }

  return null;
}

function statusLabel(status: ProductAccessStatus) {
  switch (status) {
    case 'apiOffline':
      return 'API offline';
    case 'apiError':
      return 'API attention';
    case 'tenantRequired':
      return 'Tenant required';
    case 'projectRequired':
      return 'Project required';
    case 'authInvalid':
      return 'Auth invalid';
    default:
      return 'Auth required';
  }
}

function getCreateTicketBlocker(
  apiStatus: ApiStatus,
  accessStatus: ProductAccessStatus,
  tokenConfigured: boolean,
  selectedTenantId: number | null,
  selectedProjectId: number | null,
  projectSelectionMode: 'api' | 'fallback-config' | 'missing'
) {
  if (apiStatus.status === 'disconnected') {
    return 'IronDev.Api is offline. Start the backend before creating tickets.';
  }

  if (apiStatus.status !== 'connected') {
    return 'IronDev.Api is not ready yet. Retry the connection before creating tickets.';
  }

  if (!tokenConfigured || accessStatus === 'authRequired' || accessStatus === 'authInvalid') {
    return 'Sign in or configure a valid token before creating IronDev tickets.';
  }

  if (!selectedTenantId || accessStatus === 'tenantRequired') {
    return 'Select a tenant before creating IronDev tickets.';
  }

  if (!selectedProjectId || accessStatus === 'projectRequired') {
    return 'Select a project before creating IronDev tickets.';
  }

  if (projectSelectionMode === 'fallback-config') {
    return 'Select a project explicitly before creating tickets. Fallback project context is read-only.';
  }

  if (accessStatus === 'apiError' || accessStatus === 'apiOffline') {
    return 'Resolve the current API state before creating tickets.';
  }

  return null;
}

function getTicketActionBlocker(
  apiStatus: ApiStatus,
  accessStatus: ProductAccessStatus,
  tokenConfigured: boolean,
  selectedTenantId: number | null,
  selectedProjectId: number | null,
  projectSelectionMode: 'api' | 'fallback-config' | 'missing',
  selectedTicketId: number | null
) {
  const sessionBlocker = getCreateTicketBlocker(
    apiStatus,
    accessStatus,
    tokenConfigured,
    selectedTenantId,
    selectedProjectId,
    projectSelectionMode
  );

  if (sessionBlocker) {
    return sessionBlocker;
  }

  if (!selectedTicketId) {
    return 'Select a ticket before using ticket workflow actions.';
  }

  return null;
}

function draftFromTicket(ticket: ProjectTicket): TicketEditDraft {
  return {
    title: ticket.title ?? '',
    summary: ticket.summary ?? '',
    problem: ticket.problem ?? ticket.background ?? '',
    proposedChange: ticket.content ?? '',
    type: ticket.ticketType ?? 'Feature / Workflow',
    priority: ticket.priority ?? 'Medium',
    acceptanceCriteria: ticket.acceptanceCriteria ?? '',
    technicalNotes: ticket.technicalNotes ?? '',
    unitTests: ticket.unitTests ?? '',
    integrationTests: ticket.integrationTests ?? '',
    manualTests: ticket.manualTests ?? '',
    regressionTests: ticket.regressionTests ?? '',
    buildValidation: ticket.buildValidation ?? ''
  };
}

function buildTicketFromEditDraft(ticket: ProjectTicket, draft: TicketEditDraft, projectId: number): ProjectTicket {
  return {
    ...ticket,
    projectId,
    title: draft.title.trim(),
    summary: nullIfBlank(draft.summary),
    problem: nullIfBlank(draft.problem),
    content: nullIfBlank(draft.proposedChange),
    ticketType: nullIfBlank(draft.type),
    priority: nullIfBlank(draft.priority),
    acceptanceCriteria: nullIfBlank(draft.acceptanceCriteria),
    technicalNotes: nullIfBlank(draft.technicalNotes),
    unitTests: nullIfBlank(draft.unitTests),
    integrationTests: nullIfBlank(draft.integrationTests),
    manualTests: nullIfBlank(draft.manualTests),
    regressionTests: nullIfBlank(draft.regressionTests),
    buildValidation: nullIfBlank(draft.buildValidation)
  };
}

function validateEditDraft(draft: TicketEditDraft) {
  if (!draft.title.trim()) {
    return 'Title is required before saving a ticket.';
  }

  return null;
}

function areEditDraftsEqual(left: TicketEditDraft, right: TicketEditDraft) {
  return JSON.stringify(normalizeEditDraft(left)) === JSON.stringify(normalizeEditDraft(right));
}

function normalizeEditDraft(draft: TicketEditDraft) {
  return {
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    problem: draft.problem.trim(),
    proposedChange: draft.proposedChange.trim(),
    type: draft.type.trim(),
    priority: draft.priority.trim(),
    acceptanceCriteria: normalizeMultiline(draft.acceptanceCriteria),
    technicalNotes: draft.technicalNotes.trim(),
    unitTests: draft.unitTests.trim(),
    integrationTests: draft.integrationTests.trim(),
    manualTests: draft.manualTests.trim(),
    regressionTests: draft.regressionTests.trim(),
    buildValidation: draft.buildValidation.trim()
  };
}

function normalizeMultiline(value: string) {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .join('\n');
}

function nullIfBlank(value: string) {
  const trimmed = value.trim();

  return trimmed.length > 0 ? trimmed : null;
}

function splitAcceptanceCriteria(value: string) {
  const items = value
    .split(/\r?\n/)
    .map((item) => item.replace(/^[-*]\s*/, '').trim())
    .filter(Boolean);

  return items.length > 0 ? items : undefined;
}
