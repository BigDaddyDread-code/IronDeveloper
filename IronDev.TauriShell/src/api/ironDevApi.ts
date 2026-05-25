import type { ApiStatus, ProjectTicket, TicketLoadResult } from './types';

const DEFAULT_API_BASE_URL = 'http://localhost:5000';
const DEFAULT_PROJECT_ID = 1;

export interface IronDevApiConfig {
  apiBaseUrl: string;
  requestBaseUrl: string;
  projectId: number;
  token?: string;
}

export function getIronDevApiConfig(): IronDevApiConfig {
  const configuredBaseUrl =
    import.meta.env.VITE_IRONDEV_API_BASE_URL ?? window.localStorage.getItem('irondev.apiBaseUrl');
  const apiBaseUrl = configuredBaseUrl ?? DEFAULT_API_BASE_URL;

  const rawProjectId =
    import.meta.env.VITE_IRONDEV_PROJECT_ID ??
    window.localStorage.getItem('irondev.projectId') ??
    `${DEFAULT_PROJECT_ID}`;

  const token =
    import.meta.env.VITE_IRONDEV_DEV_TOKEN ??
    window.localStorage.getItem('irondev.token') ??
    undefined;

  return {
    apiBaseUrl: apiBaseUrl.replace(/\/+$/, ''),
    requestBaseUrl: import.meta.env.DEV && !configuredBaseUrl
      ? '/irondev-api'
      : apiBaseUrl.replace(/\/+$/, ''),
    projectId: Number.parseInt(rawProjectId, 10) || DEFAULT_PROJECT_ID,
    token: token?.trim() || undefined
  };
}

export async function checkApiHealth(config: IronDevApiConfig, signal?: AbortSignal): Promise<ApiStatus> {
  try {
    const response = await fetch(`${config.requestBaseUrl}/health`, { signal });

    if (!response.ok) {
      return {
        status: 'disconnected',
        baseUrl: config.apiBaseUrl,
        message: `IronDev.Api health check returned HTTP ${response.status}.`
      };
    }

    return {
      status: 'connected',
      baseUrl: config.apiBaseUrl,
      message: 'IronDev.Api is reachable.'
    };
  } catch {
    return {
      status: 'disconnected',
      baseUrl: config.apiBaseUrl,
      message: `IronDev.Api is not reachable at ${config.apiBaseUrl}. Start it with: dotnet run --project IronDev.Api`
    };
  }
}

export async function loadProjectTickets(config: IronDevApiConfig, signal?: AbortSignal): Promise<TicketLoadResult> {
  if (!config.token) {
    return {
      tickets: [],
      status: 'unauthenticated',
      message: 'Ticket data requires an IronDev API token. Set VITE_IRONDEV_DEV_TOKEN or localStorage irondev.token.'
    };
  }

  try {
    const response = await fetch(`${config.requestBaseUrl}/api/projects/${config.projectId}/tickets`, {
      signal,
      headers: {
        Authorization: `Bearer ${config.token}`,
        Accept: 'application/json'
      }
    });

    if (response.status === 401 || response.status === 403) {
      return {
        tickets: [],
        status: 'unauthenticated',
        message: 'IronDev.Api rejected the token for ticket data.'
      };
    }

    if (!response.ok) {
      return {
        tickets: [],
        status: 'disconnected',
        message: `Ticket request failed with HTTP ${response.status}.`
      };
    }

    const tickets = (await response.json()) as ProjectTicket[];
    return {
      tickets,
      status: 'connected',
      message: tickets.length === 0 ? 'Connected. No tickets returned for this project.' : `Loaded ${tickets.length} ticket(s).`
    };
  } catch {
    return {
      tickets: [],
      status: 'disconnected',
      message: 'Ticket request could not reach IronDev.Api.'
    };
  }
}
