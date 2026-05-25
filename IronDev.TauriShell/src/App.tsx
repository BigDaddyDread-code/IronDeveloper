import { useCallback, useEffect, useMemo, useState } from 'react';
import { checkApiHealth, getIronDevApiConfig, loadProjectTickets } from './api/ironDevApi';
import type { ApiConnectionStatus, ApiStatus, ProjectTicket } from './api/types';
import { ApiStatusBadge } from './components/ApiStatusBadge';
import { AppShell } from './components/AppShell';
import { StatusBadge } from './components/StatusBadge';
import { WorkspaceHeader } from './components/WorkspaceHeader';
import { TicketsWorkspace } from './features/tickets/TicketsWorkspace';

const initialStatus: ApiStatus = {
  status: 'loading',
  baseUrl: getIronDevApiConfig().apiBaseUrl,
  message: 'Checking IronDev.Api...'
};

export default function App() {
  const config = useMemo(() => getIronDevApiConfig(), []);
  const [apiStatus, setApiStatus] = useState<ApiStatus>(initialStatus);
  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [selectedTicketId, setSelectedTicketId] = useState<number | null>(null);
  const [ticketStatus, setTicketStatus] = useState<ApiConnectionStatus>('loading');
  const [ticketMessage, setTicketMessage] = useState('Waiting for API health check.');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isTokenConfigOpen, setIsTokenConfigOpen] = useState(false);
  const [tokenDraft, setTokenDraft] = useState('');

  const tokenConfigured = Boolean(config.token);
  const ticketAccessRequiresAuth = !tokenConfigured || ticketStatus === 'authRequired';
  const selectedTicket = tickets.find((ticket) => ticket.id === selectedTicketId) ?? tickets[0] ?? null;
  const selectedTicketIdForList = selectedTicket?.id ?? null;

  const refresh = useCallback(async () => {
    const controller = new AbortController();
    setIsRefreshing(true);
    setApiStatus({ status: 'loading', baseUrl: config.apiBaseUrl, message: 'Checking IronDev.Api...' });
    setTicketStatus('loading');

    const health = await checkApiHealth(config, controller.signal);
    setApiStatus(health);

    if (health.status !== 'connected') {
      setTickets([]);
      setSelectedTicketId(null);
      setTicketStatus(health.status);
      setTicketMessage(health.message);
      setIsRefreshing(false);
      return;
    }

    const ticketResult = await loadProjectTickets(config, controller.signal);
    setTickets(ticketResult.tickets);
    setSelectedTicketId(ticketResult.tickets[0]?.id ?? null);
    setTicketStatus(ticketResult.status);
    setTicketMessage(ticketResult.message);

    setIsRefreshing(false);
  }, [config]);

  const saveToken = useCallback(() => {
    const trimmed = tokenDraft.trim();

    if (!trimmed) {
      return;
    }

    window.localStorage.setItem('irondev.token', trimmed);
    window.location.reload();
  }, [tokenDraft]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <AppShell
      header={
        <WorkspaceHeader
          apiStatus={apiStatus}
          projectId={config.projectId}
          ticketCount={tickets.length}
          tokenConfigured={tokenConfigured}
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
          {ticketAccessRequiresAuth ? (
            <StatusBadge status="authRequired">Auth required</StatusBadge>
          ) : null}
        </footer>
      }
    >
      <TicketsWorkspace
        apiStatus={apiStatus}
        apiBaseUrl={config.apiBaseUrl}
        projectId={config.projectId}
        tokenConfigured={tokenConfigured}
        ticketAccessRequiresAuth={ticketAccessRequiresAuth}
        authLabel={tokenConfigured ? 'Token rejected' : 'Missing token'}
        tickets={tickets}
        selectedTicket={selectedTicket}
        selectedTicketId={selectedTicketIdForList}
        ticketMessage={ticketMessage}
        tokenDraft={tokenDraft}
        isTokenConfigOpen={isTokenConfigOpen}
        onSelectTicket={setSelectedTicketId}
        onConfigureToken={() => setIsTokenConfigOpen((value) => !value)}
        onRetry={() => void refresh()}
        onTokenDraftChange={setTokenDraft}
        onSaveToken={saveToken}
      />
    </AppShell>
  );
}
