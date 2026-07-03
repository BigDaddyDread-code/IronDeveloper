import type {
  ApiConnectionStatus,
  ApiStatus,
  BuildReadinessResult,
  ChatCompletionRequest,
  ChatCompletionResponse,
  ChatMessage,
  ChatTurnAuditResponse,
  ControlledActionRequestCreateRequest,
  ControlledActionRequestCreateResponse,
  CreateTenantUserRequest,
  CreateTicketFromDocumentRequest,
  CreateTicketFromDocumentResponse,
  CreateProjectTicketRequest,
  DogfoodLoopApiEnvelope,
  DogfoodReceiptDetailData,
  GovernanceTraceApiEnvelope,
  GovernanceTraceDetailData,
  GovernanceTraceListData,
  GovernanceTraceQuery,
  FrontendOperationStatusReadModel,
  FrontendPatchPackageArtifactsReadModel,
  FrontendPatchPackageMetadataReadModel,
  FrontendReadinessApiEnvelope,
  ToolGateApiEnvelope,
  ToolGateDecisionDetailData,
  ToolRequestDetailData,
  RunEvidenceItem,
  RunReportDetail,
  RunReportSummary,
  EnvironmentInfo,
  LoginRequest,
  LoginResponse,
  ProjectDocument,
  ProjectDocumentVersion,
  ProjectChatSession,
  ProjectImplementationPlan,
  ProjectFileSummary,
  ProjectSummary,
  ProjectTicket,
  RunReviewPackage,
  RunTicketReviewRequest,
  RunTicketReviewResponse,
  SaveDiscussionRequest,
  SaveDiscussionResponse,
  SaveProjectChatMessageRequest,
  SaveProjectChatSessionRequest,
  StartDisposableCodeRunRequest,
  StartDisposableCodeRunResponse,
  StartTicketBuildRunRequest,
  TenantSummary,
  TenantUser,
  TicketBuildRunDto,
  TicketEvidenceSummary,
  TicketLoadResult,
  TicketRunReview,
  UserProfile,
  WorkflowReadOnlyApiEnvelope,
  WorkflowRunDetailData,
  WorkflowRunListData,
  WorkflowStepDetailData,
  WorkflowStepListData
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

  async getEnvironment(signal?: AbortSignal): Promise<EnvironmentInfo> {
    return this.request<EnvironmentInfo>('/api/environment', {
      method: 'GET',
      signal,
      skipAuth: true
    });
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

  async getTenantUsers(tenantId: number, signal?: AbortSignal): Promise<TenantUser[]> {
    return this.request<TenantUser[]>(`/api/tenants/${tenantId}/users`, { method: 'GET', signal });
  }

  async createTenantUser(tenantId: number, request: CreateTenantUserRequest, signal?: AbortSignal): Promise<TenantUser> {
    return this.request<TenantUser>(`/api/tenants/${tenantId}/users`, {
      method: 'POST',
      body: request,
      signal
    });
  }

  async setTenantUserRole(tenantId: number, userId: number, role: string, signal?: AbortSignal): Promise<void> {
    await this.request<unknown>(`/api/tenants/${tenantId}/users/${userId}/role`, {
      method: 'PUT',
      body: { role },
      signal
    });
  }

  async removeTenantUser(tenantId: number, userId: number, signal?: AbortSignal): Promise<void> {
    await this.request<unknown>(`/api/tenants/${tenantId}/users/${userId}`, {
      method: 'DELETE',
      signal
    });
  }

  async listCodeIndexFiles(
    projectId: number,
    skip = 0,
    take = 500,
    signal?: AbortSignal
  ): Promise<ProjectFileSummary[]> {
    return this.request<ProjectFileSummary[]>(
      `/api/projects/${projectId}/code-index/files?skip=${skip}&take=${take}`,
      { method: 'GET', signal }
    );
  }

  async getCodeIndexFileCount(projectId: number, signal?: AbortSignal): Promise<number> {
    return this.request<number>(`/api/projects/${projectId}/code-index/file-count`, { method: 'GET', signal });
  }

  async getProjects(signal?: AbortSignal): Promise<ProjectSummary[]> {
    return this.request<ProjectSummary[]>('/api/projects', { method: 'GET', signal });
  }

  async getProject(projectId: number, signal?: AbortSignal): Promise<ProjectSummary> {
    return this.request<ProjectSummary>(`/api/projects/${projectId}`, { method: 'GET', signal });
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

  async getProjectDocuments(projectId: number, signal?: AbortSignal): Promise<ProjectDocument[]> {
    return this.request<ProjectDocument[]>(`/api/projects/${projectId}/documents`, { method: 'GET', signal });
  }

  async getDocument(documentId: number, signal?: AbortSignal): Promise<ProjectDocument | null> {
    return this.request<ProjectDocument | null>(`/api/documents/${documentId}`, { method: 'GET', signal });
  }

  async getDocumentCurrentVersion(documentId: number, signal?: AbortSignal): Promise<ProjectDocumentVersion | null> {
    return this.request<ProjectDocumentVersion | null>(`/api/documents/${documentId}/versions/current`, {
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

  async getTicketEvidenceSummary(
    projectId: number,
    ticketId: number,
    signal?: AbortSignal
  ): Promise<TicketEvidenceSummary> {
    return this.request<TicketEvidenceSummary>(`/api/projects/${projectId}/tickets/${ticketId}/evidence-summary`, {
      method: 'GET',
      signal
    });
  }

  async startTicketBuildRun(
    projectId: number,
    ticketId: number,
    request: StartTicketBuildRunRequest = {},
    signal?: AbortSignal
  ): Promise<TicketBuildRunDto> {
    return this.request<TicketBuildRunDto>(`/api/projects/${projectId}/tickets/${ticketId}/build-runs/disposable`, {
      method: 'POST',
      body: request,
      signal
    });
  }

  async getTicketRunReview(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<TicketRunReview> {
    return this.request<TicketRunReview>(
      `/api/projects/${projectId}/tickets/${ticketId}/build-runs/${encodeURIComponent(runId)}/review`,
      {
        method: 'GET',
        signal
      }
    );
  }

  async saveDiscussion(
    projectId: number,
    request: SaveDiscussionRequest,
    signal?: AbortSignal
  ): Promise<SaveDiscussionResponse> {
    return this.request<SaveDiscussionResponse>(`/api/projects/${projectId}/discussions`, {
      method: 'POST',
      body: request,
      signal
    });
  }

  async createTicketFromDocument(
    projectId: number,
    documentVersionId: number,
    request: CreateTicketFromDocumentRequest = {},
    signal?: AbortSignal
  ): Promise<CreateTicketFromDocumentResponse> {
    return this.request<CreateTicketFromDocumentResponse>(
      `/api/projects/${projectId}/documents/${documentVersionId}/tickets`,
      {
        method: 'POST',
        body: request,
        signal
      }
    );
  }

  async reviewTicket(
    projectId: number,
    ticketId: number,
    request: RunTicketReviewRequest = { useLiveModel: false },
    signal?: AbortSignal
  ): Promise<RunTicketReviewResponse> {
    return this.request<RunTicketReviewResponse>(`/api/projects/${projectId}/tickets/${ticketId}/review`, {
      method: 'POST',
      body: request,
      signal
    });
  }

  async startDisposableCodeRun(
    projectId: number,
    ticketId: number,
    request: StartDisposableCodeRunRequest,
    signal?: AbortSignal
  ): Promise<StartDisposableCodeRunResponse> {
    return this.request<StartDisposableCodeRunResponse>(
      `/api/projects/${projectId}/tickets/${ticketId}/disposable-code-runs`,
      {
        method: 'POST',
        body: request,
        signal
      }
    );
  }

  async getRunReviewPackage(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<RunReviewPackage> {
    return this.request<RunReviewPackage>(
      `/api/projects/${projectId}/tickets/${ticketId}/build-runs/${encodeURIComponent(runId)}/review-package`,
      {
        method: 'GET',
        signal
      }
    );
  }

  async completeChat(
    projectId: number,
    request: ChatCompletionRequest,
    signal?: AbortSignal
  ): Promise<ChatCompletionResponse> {
    return this.request<ChatCompletionResponse>(`/api/projects/${projectId}/chat/complete`, {
      method: 'POST',
      body: { ...request, projectId },
      signal
    });
  }

  async getProjectChatSessions(projectId: number, signal?: AbortSignal): Promise<ProjectChatSession[]> {
    return this.request<ProjectChatSession[]>(`/api/projects/${projectId}/chat/sessions`, {
      method: 'GET',
      signal
    });
  }

  async saveProjectChatSession(
    projectId: number,
    request: SaveProjectChatSessionRequest,
    signal?: AbortSignal
  ): Promise<number> {
    return this.request<number>(`/api/projects/${projectId}/chat/sessions`, {
      method: 'POST',
      body: { ...request, projectId },
      signal
    });
  }

  async getProjectChatMessages(projectId: number, sessionId: number, signal?: AbortSignal): Promise<ChatMessage[]> {
    return this.request<ChatMessage[]>(`/api/projects/${projectId}/chat/sessions/${sessionId}/messages`, {
      method: 'GET',
      signal
    });
  }

  async getProjectChatMessageAudit(
    projectId: number,
    sessionId: number,
    messageId: number,
    signal?: AbortSignal
  ): Promise<ChatTurnAuditResponse> {
    return this.request<ChatTurnAuditResponse>(
      `/api/projects/${projectId}/chat/sessions/${sessionId}/messages/${messageId}/audit`,
      {
        method: 'GET',
        signal
      }
    );
  }

  async saveProjectChatMessage(
    projectId: number,
    sessionId: number,
    request: SaveProjectChatMessageRequest,
    signal?: AbortSignal
  ): Promise<number> {
    return this.request<number>(`/api/projects/${projectId}/chat/sessions/${sessionId}/messages`, {
      method: 'POST',
      body: { ...request, projectId, chatSessionId: sessionId },
      signal
    });
  }

  async getRunReports(signal?: AbortSignal): Promise<RunReportSummary[]> {
    return this.request<RunReportSummary[]>('/api/run-reports', { method: 'GET', signal });
  }

  async getRunReport(runId: string, signal?: AbortSignal): Promise<RunReportDetail> {
    return this.request<RunReportDetail>(`/api/run-reports/${encodeURIComponent(runId)}`, {
      method: 'GET',
      signal
    });
  }

  async getRunReportEvidence(runId: string, signal?: AbortSignal): Promise<RunEvidenceItem[]> {
    return this.request<RunEvidenceItem[]>(`/api/run-reports/${encodeURIComponent(runId)}/evidence`, {
      method: 'GET',
      signal
    });
  }

  async searchGovernanceTraces(
    query: GovernanceTraceQuery,
    signal?: AbortSignal
  ): Promise<GovernanceTraceApiEnvelope<GovernanceTraceListData>> {
    const queryString = toQueryString(query);
    return this.request<GovernanceTraceApiEnvelope<GovernanceTraceListData>>(
      `/api/v1/governance/traces${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getGovernanceTrace(
    traceId: string,
    signal?: AbortSignal
  ): Promise<GovernanceTraceApiEnvelope<GovernanceTraceDetailData>> {
    return this.request<GovernanceTraceApiEnvelope<GovernanceTraceDetailData>>(
      `/api/v1/governance/traces/${encodeURIComponent(traceId)}`,
      { method: 'GET', signal }
    );
  }

  async getGovernanceTraceByCorrelation(
    correlationId: string,
    projectReferenceId = '',
    signal?: AbortSignal
  ): Promise<GovernanceTraceApiEnvelope<GovernanceTraceDetailData>> {
    const queryString = toQueryString({ projectReferenceId });
    return this.request<GovernanceTraceApiEnvelope<GovernanceTraceDetailData>>(
      `/api/v1/governance/traces/by-correlation/${encodeURIComponent(correlationId)}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getGovernanceTraceByWorkflowRun(
    workflowRunId: string,
    projectReferenceId: string,
    signal?: AbortSignal
  ): Promise<GovernanceTraceApiEnvelope<GovernanceTraceListData>> {
    const queryString = toQueryString({ projectReferenceId });
    return this.request<GovernanceTraceApiEnvelope<GovernanceTraceListData>>(
      `/api/v1/governance/traces/by-workflow-run/${encodeURIComponent(workflowRunId)}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async listWorkflowRuns(
    projectId: string | number,
    take: string | number,
    signal?: AbortSignal
  ): Promise<WorkflowReadOnlyApiEnvelope<WorkflowRunListData>> {
    const queryString = toQueryString({ projectId, take });
    return this.request<WorkflowReadOnlyApiEnvelope<WorkflowRunListData>>(
      `/api/v1/workflow/runs${queryString}`,
      { method: 'GET', signal }
    );
  }

  async listWorkflowRunsByCorrelation(
    correlationId: string,
    projectId: string | number,
    take: string | number,
    signal?: AbortSignal
  ): Promise<WorkflowReadOnlyApiEnvelope<WorkflowRunListData>> {
    const queryString = toQueryString({ projectId, take });
    return this.request<WorkflowReadOnlyApiEnvelope<WorkflowRunListData>>(
      `/api/v1/workflow/runs/by-correlation/${encodeURIComponent(correlationId)}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getWorkflowRun(
    workflowRunId: string,
    projectId: string | number,
    signal?: AbortSignal
  ): Promise<WorkflowReadOnlyApiEnvelope<WorkflowRunDetailData>> {
    const queryString = toQueryString({ projectId });
    return this.request<WorkflowReadOnlyApiEnvelope<WorkflowRunDetailData>>(
      `/api/v1/workflow/runs/${encodeURIComponent(workflowRunId)}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async listWorkflowSteps(
    workflowRunId: string,
    projectId: string | number,
    take: string | number,
    signal?: AbortSignal
  ): Promise<WorkflowReadOnlyApiEnvelope<WorkflowStepListData>> {
    const queryString = toQueryString({ projectId, take });
    return this.request<WorkflowReadOnlyApiEnvelope<WorkflowStepListData>>(
      `/api/v1/workflow/runs/${encodeURIComponent(workflowRunId)}/steps${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getWorkflowStep(
    workflowRunId: string,
    workflowRunStepId: string,
    projectId: string | number,
    signal?: AbortSignal
  ): Promise<WorkflowReadOnlyApiEnvelope<WorkflowStepDetailData>> {
    const queryString = toQueryString({ projectId });
    return this.request<WorkflowReadOnlyApiEnvelope<WorkflowStepDetailData>>(
      `/api/v1/workflow/runs/${encodeURIComponent(workflowRunId)}/steps/${encodeURIComponent(
        workflowRunStepId
      )}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getFrontendOperationStatus(
    operationId: string,
    compact: boolean,
    signal?: AbortSignal
  ): Promise<FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>> {
    const queryString = toQueryString({ compact });
    return this.request<FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>>(
      `/api/frontend-readiness/operations/${encodeURIComponent(operationId)}/status${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getFrontendPatchPackageMetadata(
    packageId: string,
    compact: boolean,
    signal?: AbortSignal
  ): Promise<FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>> {
    const queryString = toQueryString({ compact });
    return this.request<FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>>(
      `/api/frontend-readiness/patch-packages/${encodeURIComponent(packageId)}/metadata${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getFrontendPatchPackageArtifacts(
    packageId: string,
    compact: boolean,
    signal?: AbortSignal
  ): Promise<FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>> {
    const queryString = toQueryString({ compact });
    return this.request<FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>>(
      `/api/frontend-readiness/patch-packages/${encodeURIComponent(packageId)}/artifacts${queryString}`,
      { method: 'GET', signal }
    );
  }

  async createFrontendControlledActionRequest(
    request: ControlledActionRequestCreateRequest,
    signal?: AbortSignal
  ): Promise<ControlledActionRequestCreateResponse> {
    return this.request<ControlledActionRequestCreateResponse>('/api/frontend-readiness/action-requests', {
      method: 'POST',
      body: request,
      signal
    });
  }

  async getDogfoodLoopReceipt(
    dogfoodLoopId: string,
    projectId: string | number,
    signal?: AbortSignal
  ): Promise<DogfoodLoopApiEnvelope<DogfoodReceiptDetailData>> {
    const queryString = toQueryString({ projectId });
    return this.request<DogfoodLoopApiEnvelope<DogfoodReceiptDetailData>>(
      `/api/v1/dogfood-loops/${encodeURIComponent(dogfoodLoopId)}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getToolRequest(
    toolRequestId: string,
    projectId: string | number,
    signal?: AbortSignal
  ): Promise<ToolGateApiEnvelope<ToolRequestDetailData>> {
    const queryString = toQueryString({ projectId });
    return this.request<ToolGateApiEnvelope<ToolRequestDetailData>>(
      `/api/v1/tool-requests/${encodeURIComponent(toolRequestId)}${queryString}`,
      { method: 'GET', signal }
    );
  }

  async getToolGateDecision(
    gateDecisionId: string,
    projectId: string | number,
    signal?: AbortSignal
  ): Promise<ToolGateApiEnvelope<ToolGateDecisionDetailData>> {
    const queryString = toQueryString({ projectId });
    return this.request<ToolGateApiEnvelope<ToolGateDecisionDetailData>>(
      `/api/v1/tool-gates/evaluations/${encodeURIComponent(gateDecisionId)}${queryString}`,
      { method: 'GET', signal }
    );
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

function toQueryString(query: object) {
  const parameters = new URLSearchParams();

  for (const [key, value] of Object.entries(query as Record<string, string | number | boolean | null | undefined>)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }

    parameters.set(key, `${value}`);
  }

  const text = parameters.toString();
  return text ? `?${text}` : '';
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
