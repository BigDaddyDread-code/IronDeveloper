import { useCallback, useEffect, useMemo, useState } from 'react';
import { checkApiHealth, getIronDevApiConfig, loadProjectTickets } from './api/ironDevApi';
import type { ApiStatus, ProjectTicket } from './api/types';
import { ContextInspector } from './components/ContextInspector';
import { StatusBadge } from './components/StatusBadge';
import { TicketDetail } from './components/TicketDetail';
import { TicketList } from './components/TicketList';
import { WorkspaceHeader } from './components/WorkspaceHeader';
import { WorkspaceShell } from './components/WorkspaceShell';

const initialStatus: ApiStatus = {
  status: 'checking',
  baseUrl: getIronDevApiConfig().apiBaseUrl,
  message: 'Checking IronDev.Api...'
};

export default function App() {
  const config = useMemo(() => getIronDevApiConfig(), []);
  const [apiStatus, setApiStatus] = useState<ApiStatus>(initialStatus);
  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [selectedTicketId, setSelectedTicketId] = useState<number | null>(null);
  const [ticketMessage, setTicketMessage] = useState('Waiting for API health check.');
  const [isRefreshing, setIsRefreshing] = useState(false);

  const selectedTicket = tickets.find((ticket) => ticket.id === selectedTicketId) ?? tickets[0] ?? null;

  const refresh = useCallback(async () => {
    const controller = new AbortController();
    setIsRefreshing(true);
    setApiStatus({ status: 'checking', baseUrl: config.apiBaseUrl, message: 'Checking IronDev.Api...' });

    const health = await checkApiHealth(config, controller.signal);
    setApiStatus(health);

    if (health.status !== 'connected') {
      setTickets([]);
      setSelectedTicketId(null);
      setTicketMessage(health.message);
      setIsRefreshing(false);
      return;
    }

    const ticketResult = await loadProjectTickets(config, controller.signal);
    setTickets(ticketResult.tickets);
    setSelectedTicketId(ticketResult.tickets[0]?.id ?? null);
    setTicketMessage(ticketResult.message);

    setIsRefreshing(false);
  }, [config]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <div className="app-shell" data-testid="app.shell">
      <WorkspaceHeader
        apiStatus={apiStatus}
        projectId={config.projectId}
        isRefreshing={isRefreshing}
        onRefresh={() => void refresh()}
      />

      <nav className="shell-nav" aria-label="Workspace navigation">
        <button className="shell-nav__item shell-nav__item--active" data-testid="shell.nav.tickets">
          Tickets
        </button>
      </nav>

      <main className="tickets-workspace" data-testid="tickets.workspace">
        <WorkspaceShell
          left={
            <TicketList
              tickets={tickets}
              selectedTicketId={selectedTicket?.id ?? null}
              message={ticketMessage}
              onSelect={setSelectedTicketId}
            />
          }
          center={<TicketDetail ticket={selectedTicket} />}
          right={
            <ContextInspector
              ticket={selectedTicket}
              apiBaseUrl={config.apiBaseUrl}
              projectId={config.projectId}
              tokenConfigured={Boolean(config.token)}
            />
          }
        />
      </main>

      <footer className="shell-footer">
        <StatusBadge status={apiStatus.status}>{apiStatus.status}</StatusBadge>
        <span>{apiStatus.message}</span>
      </footer>
    </div>
  );
}
