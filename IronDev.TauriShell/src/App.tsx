import { useCallback, useEffect, useMemo, useState } from 'react';
import { createIronDevApiClient, getIronDevApiConfig, IronDevApiError } from './api/ironDevApi';
import type {
  ApiStatus,
  ProductAccessStatus,
  ProjectSummary,
  ProjectTicket,
  TenantSummary,
  UserProfile
} from './api/types';
import { ApiStatusBadge } from './components/ApiStatusBadge';
import { AppShell } from './components/AppShell';
import { StatusBadge } from './components/StatusBadge';
import { WorkspaceHeader } from './components/WorkspaceHeader';
import { TicketsWorkspace } from './features/tickets/TicketsWorkspace';

const initialConfig = getIronDevApiConfig();

const initialStatus: ApiStatus = {
  status: 'loading',
  baseUrl: initialConfig.apiBaseUrl,
  message: 'Checking IronDev.Api...'
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
  const [selectedTenantId, setSelectedTenantId] = useState<number | null>(config.selectedTenantId ?? null);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(config.selectedProjectId ?? null);
  const [selectedProjectName, setSelectedProjectName] = useState<string | null>(null);
  const [projectSelectionMode, setProjectSelectionMode] = useState<'api' | 'fallback-config'>('fallback-config');
  const [ticketMessage, setTicketMessage] = useState('Waiting for API health check.');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isTokenConfigOpen, setIsTokenConfigOpen] = useState(false);
  const [tokenDraft, setTokenDraft] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const tokenConfigured = Boolean(config.token);
  const selectedTicket = tickets.find((ticket) => ticket.id === selectedTicketId) ?? tickets[0] ?? null;
  const selectedTicketIdForList = selectedTicket?.id ?? null;
  const productAccessBlocked = !['ready', 'emptyTickets', 'loadingTickets'].includes(accessStatus);
  const projectBadgeStatus = selectedProjectId ? 'selected' : 'missing';

  const refreshConfig = useCallback(() => {
    setConfigVersion((value) => value + 1);
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
      setTickets([]);
      setSelectedTicketId(null);
      setAccessStatus(health.status === 'disconnected' ? 'apiOffline' : 'apiError');
      setTicketMessage(health.message);
      setIsRefreshing(false);
      return;
    }

    if (!config.token) {
      setTickets([]);
      setSelectedTicketId(null);
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
        setTickets([]);
        setSelectedTicketId(null);
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
      setProjectSelectionMode(config.selectedProjectId ? 'api' : 'fallback-config');

      if (!projectId) {
        setTickets([]);
        setSelectedTicketId(null);
        setAccessStatus('projectRequired');
        setTicketMessage('Select a project before loading tickets.');
        setIsRefreshing(false);
        return;
      }

      await client.selectProject(projectId, controller.signal).catch(() => undefined);

      setAccessStatus('loadingTickets');
      setTicketMessage('Loading tickets...');
      const ticketResult = await client.getProjectTickets(projectId, controller.signal);
      setTickets(ticketResult.tickets);
      setSelectedTicketId(ticketResult.tickets[0]?.id ?? null);
      setTicketMessage(ticketResult.message);
      setAccessStatus(ticketResult.tickets.length === 0 ? 'emptyTickets' : 'ready');
    } catch (error) {
      setTickets([]);
      setSelectedTicketId(null);

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
  }, [client, config]);

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

  return (
    <AppShell
      header={
        <WorkspaceHeader
          apiStatus={apiStatus}
          projectId={selectedProjectId ?? config.fallbackProjectId}
          projectName={selectedProjectName}
          projectStatus={projectBadgeStatus}
          projectSelectionMode={projectSelectionMode}
          ticketCount={tickets.length}
          tokenConfigured={tokenConfigured}
          userDisplayName={userProfile?.displayName ?? null}
          tenantName={tenants.find((tenant) => tenant.id === selectedTenantId)?.name ?? null}
          isRefreshing={isRefreshing}
          onRefresh={() => void refresh()}
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
        projectId={selectedProjectId ?? config.fallbackProjectId}
        tokenConfigured={tokenConfigured}
        productAccessBlocked={productAccessBlocked}
        authLabel={tokenConfigured ? 'Token rejected' : 'Missing token'}
        tenants={tenants}
        projects={projects}
        selectedTenantId={selectedTenantId}
        selectedProjectId={selectedProjectId}
        tickets={tickets}
        selectedTicket={selectedTicket}
        selectedTicketId={selectedTicketIdForList}
        ticketMessage={ticketMessage}
        tokenDraft={tokenDraft}
        email={email}
        password={password}
        isTokenConfigOpen={isTokenConfigOpen}
        isBusy={isRefreshing}
        errorMessage={errorMessage}
        onSelectTicket={setSelectedTicketId}
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
  return (
    projects.find((project) => project.id === selectedProjectId) ??
    projects.find((project) => project.id === fallbackProjectId) ??
    projects[0] ??
    null
  );
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
