import type {
  ApiConnectionStatus,
  ApiStatus,
  BuildReadinessResult,
  CreateProjectTicketRequest,
  LoginRequest,
  LoginResponse,
  ProjectImplementationPlan,
  ProjectSummary,
  ProjectTicket,
  TenantSummary,
  TicketLoadResult,
  UserProfile
} from './types';

const DEFAULT_API_BASE_URL = 'http://localhost:5000';
const DEFAULT_PROJECT_ID = 1;

export interface IronDevApiConfig {
  apiBaseUrl: string;
  requestBaseUrl: string;
  fallbackProjectId: number;
  token?: string;
  selectedTenantId?: number;
  selectedProjectId?: number;
}

export class IronDevApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly body?: unknown
  ) {
    super(message);
    this.name = 'IronDevApiError';
  }

  get isAuthFailure() {
    return this.status === 401 || this.status === 403;
  }
}

export function getIronDevApiConfig(): IronDevApiConfig {
  const configuredBaseUrl =
    import.meta.env.VITE_IRONDEV_API_BASE_URL ?? window.localStorage.getItem('irondev.apiBaseUrl');
  const apiBaseUrl = (configuredBaseUrl?.trim() || DEFAULT_API_BASE_URL).replace(/\/+$/, '');
  const shouldUseViteProxy = import.meta.env.DEV && isDefaultLocalApi(apiBaseUrl);

  const rawFallbackProjectId =
    import.meta.env.VITE_IRONDEV_PROJECT_ID ??
    window.localStorage.getItem('irondev.projectId') ??
    `${DEFAULT_PROJECT_ID}`;

  const token =
    import.meta.env.VITE_IRONDEV_DEV_TOKEN ?? window.localStorage.getItem('irondev.token') ?? undefined;

  const selectedTenantId = parseOptionalInt(window.localStorage.getItem('irondev.tenantId'));
  const selectedProjectId = parseOptionalInt(window.localStorage.getItem('irondev.selectedProjectId'));

  return {
    apiBaseUrl,
    requestBaseUrl: shouldUseViteProxy ? '/irondev-api' : apiBaseUrl,
    fallbackProjectId: Number.parseInt(rawFallbackProjectId, 10) || DEFAULT_PROJECT_ID,
    token: token?.trim() || undefined,
    selectedTenantId,
    selectedProjectId
  };
}

export function createIronDevApiClient(config: IronDevApiConfig) {
  return new IronDevApiClient(config);
}

export async function checkApiHealth(config: IronDevApiConfig, signal?: AbortSignal): Promise<ApiStatus> {
  return createIronDevApiClient(config).checkHealth(signal);
}

export async function loadProjectTickets(config: IronDevApiConfig, signal?: AbortSignal): Promise<TicketLoadResult> {
  if (!config.token) {
    return {
      tickets: [],
      status: 'authRequired',
      message: 'Ticket data requires an IronDev API token. Set VITE_IRONDEV_DEV_TOKEN or localStorage irondev.token.'
    };
  }

  if (!config.selectedProjectId) {
    return {
      tickets: [],
      status: 'error',
      message: 'Select a project before loading tickets.'
    };
  }

  return createIronDevApiClient(config).getProjectTickets(config.selectedProjectId, signal);
}

class IronDevApiClient {
  constructor(private readonly config: IronDevApiConfig) {}

  async checkHealth(signal?: AbortSignal): Promise<ApiStatus> {
    try {
      const response = await fetch(`${this.config.requestBaseUrl}/health`, { signal });

      if (!response.ok) {
        return {
          status: 'error',
          baseUrl: this.config.apiBaseUrl,
          message: `IronDev.Api responded, but health is not passing (HTTP ${response.status}). Check the local API process.`
        };
      }

      return {
        status: 'connected',
        baseUrl: this.config.apiBaseUrl,
        message: 'IronDev.Api is reachable.'
      };
    } catch {
      return {
        status: 'disconnected',
        baseUrl: this.config.apiBaseUrl,
        message: `IronDev.Api is not reachable at ${this.config.apiBaseUrl}. Start it with: dotnet run --project IronDev.Api`
      };
    }
  }

  async login(request: LoginRequest, signal?: AbortSignal): Promise<LoginResponse> {
    return this.request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: request,
      signal,
      skipAuth: true
    });
  }

  async getCurrentUser(signal?: AbortSignal): Promise<UserProfile> {
    return this.request<UserProfile>('/api/auth/me', { method: 'GET', signal });
  }

  async getTenants(signal?: AbortSignal): Promise<TenantSummary[]> {
    return this.request<TenantSummary[]>('/api/tenants', { method: 'GET', signal });
  }

  async selectTenant(tenantId: number, signal?: AbortSignal): Promise<LoginResponse> {
    return this.request<LoginResponse>('/api/tenants/select', {
      method: 'POST',
      body: { tenantId },
      signal
    });
  }

  async getProjects(signal?: AbortSignal): Promise<ProjectSummary[]> {
    return this.request<ProjectSummary[]>('/api/projects', { method: 'GET', signal });
  }

  async selectProject(projectId: number, signal?: AbortSignal): Promise<{ projectId: number }> {
    return this.request<{ projectId: number }>(`/api/projects/${projectId}/select`, { method: 'POST', signal });
  }

  async getProjectTickets(projectId: number, signal?: AbortSignal): Promise<TicketLoadResult> {
    try {
      const tickets = await this.request<ProjectTicket[]>(`/api/projects/${projectId}/tickets`, {
        method: 'GET',
        signal
      });

      return {
        tickets,
        status: 'connected',
        message: tickets.length === 0 ? 'Connected. No tickets returned for this project.' : `Loaded ${tickets.length} ticket(s).`
      };
    } catch (error) {
      if (error instanceof IronDevApiError && error.isAuthFailure) {
        return {
          tickets: [],
          status: 'authRequired',
          message: 'IronDev.Api rejected the token for ticket data.'
        };
      }

      if (error instanceof IronDevApiError) {
        return {
          tickets: [],
          status: 'error',
          message: `Ticket request failed with HTTP ${error.status}.`
        };
      }

      return {
        tickets: [],
        status: 'disconnected',
        message: 'Ticket request could not reach IronDev.Api.'
      };
    }
  }

  async createProjectTicket(
    projectId: number,
    request: CreateProjectTicketRequest,
    signal?: AbortSignal
  ): Promise<ProjectTicket> {
    return this.request<ProjectTicket>(`/api/projects/${projectId}/tickets`, {
      method: 'POST',
      body: request,
      signal
    });
  }

  async saveProjectTicket(projectId: number, ticket: ProjectTicket, signal?: AbortSignal): Promise<ProjectTicket> {
    return this.request<ProjectTicket>(`/api/projects/${projectId}/tickets/legacy`, {
      method: 'POST',
      body: ticket,
      signal
    });
  }

  async getProjectTicket(projectId: number, ticketId: number, signal?: AbortSignal): Promise<ProjectTicket> {
    return this.request<ProjectTicket>(`/api/projects/${projectId}/tickets/${ticketId}`, {
      method: 'GET',
      signal
    });
  }

  async getTicketImplementationPlan(ticketId: number, signal?: AbortSignal): Promise<ProjectImplementationPlan> {
    return this.request<ProjectImplementationPlan>(`/api/tickets/${ticketId}/implementation-plan`, {
      method: 'GET',
      signal
    });
  }

  async getTicketBuildReadiness(
    projectId: number,
    ticketId: number,
    signal?: AbortSignal
  ): Promise<BuildReadinessResult> {
    return this.request<BuildReadinessResult>(`/api/projects/${projectId}/tickets/${ticketId}/build-readiness`, {
      method: 'GET',
      signal
    });
  }

  private async request<T>(path: string, options: RequestOptions): Promise<T> {
    const headers = new Headers({ Accept: 'application/json' });

    if (options.body !== undefined) {
      headers.set('Content-Type', 'application/json');
    }

    if (!options.skipAuth && this.config.token) {
      headers.set('Authorization', `Bearer ${this.config.token}`);
    }

    const response = await fetch(`${this.config.requestBaseUrl}${path}`, {
      method: options.method,
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal
    });

    if (!response.ok) {
      throw new IronDevApiError(`IronDev.Api request failed with HTTP ${response.status}.`, response.status, await readBody(response));
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}

interface RequestOptions {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
  skipAuth?: boolean;
}

function parseOptionalInt(value: string | null) {
  if (!value) {
    return undefined;
  }

  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function isDefaultLocalApi(value: string) {
  try {
    const url = new URL(value);

    return url.protocol === 'http:' && (url.hostname === 'localhost' || url.hostname === '127.0.0.1') && url.port === '5000';
  } catch {
    return value === DEFAULT_API_BASE_URL;
  }
}

async function readBody(response: Response) {
  const text = await response.text();

  if (!text) {
    return undefined;
  }

  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export function toApiStatus(status: ApiConnectionStatus, baseUrl: string, message: string): ApiStatus {
  return { status, baseUrl, message };
}
