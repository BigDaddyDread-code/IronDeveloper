import type {
  AcceptedApprovalEnvelope,
  AcceptedApprovalReadModelUi,
  ApiConnectionStatus,
  ApiStatus,
  BuildReadinessResult,
  CreateAcceptedApprovalUiRequest,
  SkeletonAgentProfile,
  SkeletonAgentProfileOutcome,
  SkeletonAgentProfileUpdate,
  SkeletonBatchMapOutcome,
  SkeletonBatchPlanOutcome,
  SkeletonBatchRunOutcome,
  SkeletonBatchRunStatus,
  SkeletonCriticPackage,
  SkeletonCriticReviewOutcome,
  SkeletonFindingDispositionOutcome,
  SkeletonGateRecommendation,
  SkeletonRunReport,
  ChatCompletionRequest,
  ChatCompletionResponse,
  ChatDocumentSource,
  ChatMessage,
  ChatTurnAuditResponse,
  ConfirmBaWorkingDraftRequest,
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
  PlannedSurfaceEnvelope,
  ProjectProvisioningReadinessUi,
  ProjectDocument,
  ProjectDocumentProcessingResult,
  ProjectDocumentUploadResult,
  ProjectDocumentVersion,
  ProjectToolCatalogueResponse,
  ProjectToolDetailResponse,
  ProjectMemberDirectoryResponse,
  SetProjectChannelMembershipRequest,
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
  fallbackProjectId: number | null;
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
  const fallbackProjectId = parseFallbackProjectId(rawFallbackProjectId);

  const token =
    import.meta.env.VITE_IRONDEV_DEV_TOKEN ?? window.localStorage.getItem('irondev.token') ?? undefined;

  const selectedTenantId = parseOptionalInt(window.localStorage.getItem('irondev.tenantId'));
  const selectedProjectId = parseOptionalInt(window.localStorage.getItem('irondev.selectedProjectId'));

  return {
    apiBaseUrl,
    requestBaseUrl: shouldUseViteProxy ? '/irondev-api' : apiBaseUrl,
    fallbackProjectId,
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

  async logout(signal?: AbortSignal): Promise<void> {
    await this.request<unknown>('/api/auth/logout', { method: 'POST', signal });
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
    const response = await this.request<TenantUser[] | TenantUser | null>(`/api/tenants/${tenantId}/users`, {
      method: 'GET',
      signal
    });
    return Array.isArray(response) ? response : response === null ? [] : [response];
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

  /**
   * UX-START-0: creates a governed project shell. A created project is a
   * context boundary, not readiness — the caller lands on the readiness
   * screen next, never straight into governed work.
   */
  async createProject(name: string, localPath: string, signal?: AbortSignal): Promise<ProjectSummary> {
    return this.request<ProjectSummary>('/api/projects', {
      method: 'POST',
      body: { name, description: '', localPath },
      signal
    });
  }

  async updateProjectLocalPath(projectId: number, localPath: string, signal?: AbortSignal): Promise<void> {
    await this.request<unknown>(`/api/projects/${projectId}/local-path`, {
      method: 'PUT',
      body: { localPath },
      signal
    });
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

  async confirmBaWorkingDraft(
    projectId: number,
    request: ConfirmBaWorkingDraftRequest,
    signal?: AbortSignal
  ): Promise<ProjectTicket> {
    return this.request<ProjectTicket>(`/api/projects/${projectId}/tickets/ba-draft/confirm`, {
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

  async getProjectDocuments(projectId: number, status = '*', signal?: AbortSignal): Promise<ProjectDocument[]> {
    const query = new URLSearchParams({ status });
    return this.request<ProjectDocument[]>(`/api/projects/${projectId}/documents?${query}`, { method: 'GET', signal });
  }

  async uploadProjectDocument(
    projectId: number,
    input: {
      file: File;
      displayName: string;
      documentType: string;
      description?: string;
    },
    signal?: AbortSignal
  ): Promise<ProjectDocumentUploadResult> {
    const form = new FormData();
    form.set('file', input.file, input.file.name);
    form.set('displayName', input.displayName);
    form.set('documentType', input.documentType);
    if (input.description?.trim()) {
      form.set('description', input.description.trim());
    }

    return this.request<ProjectDocumentUploadResult>(`/api/projects/${projectId}/documents/upload`, {
      method: 'POST',
      rawBody: form,
      signal
    });
  }

  async getProjectDocument(projectId: number, documentId: number, signal?: AbortSignal): Promise<ProjectDocument> {
    return this.request<ProjectDocument>(`/api/projects/${projectId}/documents/${documentId}`, {
      method: 'GET',
      signal
    });
  }

  async processProjectDocument(
    projectId: number,
    documentId: number,
    signal?: AbortSignal
  ): Promise<ProjectDocumentProcessingResult> {
    return this.request<ProjectDocumentProcessingResult>(
      `/api/projects/${projectId}/documents/${documentId}/process`,
      { method: 'POST', body: {}, signal }
    );
  }

  async getProjectDocumentCurrentVersion(
    projectId: number,
    documentId: number,
    signal?: AbortSignal
  ): Promise<ProjectDocumentVersion> {
    return this.request<ProjectDocumentVersion>(
      `/api/projects/${projectId}/documents/${documentId}/versions/current`,
      { method: 'GET', signal }
    );
  }

  async getProjectDocumentVersions(
    projectId: number,
    documentId: number,
    signal?: AbortSignal
  ): Promise<ProjectDocumentVersion[]> {
    return this.request<ProjectDocumentVersion[]>(`/api/projects/${projectId}/documents/${documentId}/versions`, {
      method: 'GET',
      signal
    });
  }

  async getProjectTools(projectId: number, signal?: AbortSignal): Promise<ProjectToolCatalogueResponse> {
    return this.request<ProjectToolCatalogueResponse>(`/api/projects/${projectId}/tools`, {
      method: 'GET',
      signal
    });
  }

  async getProjectTool(projectId: number, toolId: string, signal?: AbortSignal): Promise<ProjectToolDetailResponse> {
    return this.request<ProjectToolDetailResponse>(
      `/api/projects/${projectId}/tools/${encodeURIComponent(toolId)}`,
      { method: 'GET', signal }
    );
  }

  async getProjectMembers(projectId: number, signal?: AbortSignal): Promise<ProjectMemberDirectoryResponse> {
    return this.request<ProjectMemberDirectoryResponse>(`/api/projects/${projectId}/members`, {
      method: 'GET',
      signal
    });
  }

  async setProjectChannelMembership(
    projectId: number,
    channelId: number,
    userId: number,
    request: SetProjectChannelMembershipRequest
  ): Promise<void> {
    await this.request(`/api/projects/${projectId}/channels/${channelId}/members/${userId}`, {
      method: 'PUT',
      body: request
    });
  }

  async removeProjectChannelMembership(projectId: number, channelId: number, userId: number): Promise<void> {
    await this.request(`/api/projects/${projectId}/channels/${channelId}/members/${userId}`, {
      method: 'DELETE'
    });
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

  async getProjectChatDocumentSources(projectId: number, signal?: AbortSignal): Promise<ChatDocumentSource[]> {
    return this.request<ChatDocumentSource[]>(`/api/projects/${projectId}/chat/document-sources`, {
      method: 'GET',
      signal
    });
  }

  async getProjectChatSession(
    projectId: number,
    sessionId: number,
    signal?: AbortSignal
  ): Promise<ProjectChatSession | undefined> {
    return this.request<ProjectChatSession | undefined>(
      `/api/projects/${projectId}/chat/sessions/${sessionId}`,
      { method: 'GET', signal }
    );
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

  // ── P0-7: skeleton run — every action below is a REQUEST to a governed
  // endpoint that enforces its own gate. The UI records and requests; the
  // backend verifies and refuses. UI visibility is not backend authority.

  async startSkeletonRun(projectId: number, ticketId: number, signal?: AbortSignal): Promise<TicketBuildRunDto> {
    return this.request<TicketBuildRunDto>(`/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs`, {
      method: 'POST',
      signal
    });
  }

  async requestSkeletonRunContinuation(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<TicketBuildRunDto> {
    return this.request<TicketBuildRunDto>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/continue`,
      { method: 'POST', signal }
    );
  }

  async requestSkeletonRunApply(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<TicketBuildRunDto> {
    return this.request<TicketBuildRunDto>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/apply`,
      { method: 'POST', signal }
    );
  }

  async getSkeletonCriticPackage(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<SkeletonCriticPackage> {
    return this.request<SkeletonCriticPackage>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/critic-package`,
      { method: 'GET', signal }
    );
  }

  async getSkeletonRunReport(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<SkeletonRunReport> {
    return this.request<SkeletonRunReport>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/report`,
      { method: 'GET', signal }
    );
  }

  // AG-1: agent profiles — read and edit the model + voice each agent runs on.
  // Voice and model only; the backend refuses secrets and grants no authority.

  async listAgentProfiles(signal?: AbortSignal): Promise<SkeletonAgentProfile[]> {
    const response = await this.request<RawSkeletonAgentProfile[]>('/api/v1/agent-profiles', { method: 'GET', signal });
    return response.map(normalizeSkeletonAgentProfile);
  }

  async updateAgentProfile(
    role: string,
    update: SkeletonAgentProfileUpdate,
    signal?: AbortSignal
  ): Promise<SkeletonAgentProfileOutcome> {
    return this.request<SkeletonAgentProfileOutcome>(`/api/v1/agent-profiles/${encodeURIComponent(role)}`, {
      method: 'PUT',
      body: update,
      signal
    });
  }

  // P2: batch — detect the dependency map, sequence it into a plan, run it wave
  // by wave, advance it, and read policy's advisory gate recommendation. Every
  // call is a REQUEST to a governed endpoint; the backend owns every decision.

  async detectBatchMap(projectId: number, ticketIds: number[], signal?: AbortSignal): Promise<SkeletonBatchMapOutcome> {
    return this.request<SkeletonBatchMapOutcome>(`/api/projects/${projectId}/batch-maps`, {
      method: 'POST',
      body: { ticketIds },
      signal
    });
  }

  async planBatch(projectId: number, mapId: string, signal?: AbortSignal): Promise<SkeletonBatchPlanOutcome> {
    return this.request<SkeletonBatchPlanOutcome>(`/api/projects/${projectId}/batch-plans`, {
      method: 'POST',
      body: { mapId },
      signal
    });
  }

  async startBatchRun(projectId: number, planId: string, signal?: AbortSignal): Promise<SkeletonBatchRunOutcome> {
    return this.request<SkeletonBatchRunOutcome>(`/api/projects/${projectId}/batch-runs`, {
      method: 'POST',
      body: { planId },
      signal
    });
  }

  async advanceBatchRun(projectId: number, batchId: string, signal?: AbortSignal): Promise<SkeletonBatchRunOutcome> {
    return this.request<SkeletonBatchRunOutcome>(
      `/api/projects/${projectId}/batch-runs/${encodeURIComponent(batchId)}/advance`,
      { method: 'POST', signal }
    );
  }

  async getBatchRun(projectId: number, batchId: string, signal?: AbortSignal): Promise<SkeletonBatchRunStatus> {
    return this.request<SkeletonBatchRunStatus>(
      `/api/projects/${projectId}/batch-runs/${encodeURIComponent(batchId)}`,
      { method: 'GET', signal }
    );
  }

  async getGateRecommendation(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<SkeletonGateRecommendation> {
    return this.request<SkeletonGateRecommendation>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/gate-recommendation`,
      { method: 'GET', signal }
    );
  }

  // P1-7: the critic's own surface and the disposition surface. A review is
  // advisory (a finding is not a veto); a disposition is a human decision about
  // a finding (it is not approval). The backend enforces both invariants.

  async requestSkeletonCriticReview(
    projectId: number,
    ticketId: number,
    runId: string,
    signal?: AbortSignal
  ): Promise<SkeletonCriticReviewOutcome> {
    return this.request<SkeletonCriticReviewOutcome>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/critic-review`,
      { method: 'POST', signal }
    );
  }

  async recordFindingDisposition(
    projectId: number,
    ticketId: number,
    runId: string,
    findingId: string,
    disposition: string,
    reason: string,
    signal?: AbortSignal
  ): Promise<SkeletonFindingDispositionOutcome> {
    return this.request<SkeletonFindingDispositionOutcome>(
      `/api/projects/${projectId}/tickets/${ticketId}/skeleton-runs/${encodeURIComponent(runId)}/findings/${encodeURIComponent(findingId)}/disposition`,
      { method: 'POST', body: { disposition, reason }, signal }
    );
  }

  // The human gate's governed surface. The server owns identity, timestamps,
  // and every derived authority field; recording is not policy satisfaction,
  // not continuation, not apply. approvalProjectGuidFor maps the int project id
  // into the governance scope the accepted-approvals surface is keyed by.
  async recordAcceptedApproval(
    projectId: number,
    request: CreateAcceptedApprovalUiRequest,
    signal?: AbortSignal
  ): Promise<AcceptedApprovalEnvelope<AcceptedApprovalReadModelUi>> {
    return this.request<AcceptedApprovalEnvelope<AcceptedApprovalReadModelUi>>(
      `/api/v1/projects/${approvalProjectGuidFor(projectId)}/accepted-approvals`,
      { method: 'POST', body: request, signal }
    );
  }

  // ── PROJECT-0..3: provisioning readiness + wizard confirmations ──

  async getProvisioningReadiness(projectId: number, signal?: AbortSignal): Promise<ProjectProvisioningReadinessUi> {
    return this.request<ProjectProvisioningReadinessUi>(`/api/projects/${projectId}/provisioning/readiness`, {
      method: 'GET',
      signal
    });
  }

  /** Confirming a command is a human decision entering stored truth — the wizard's pointed question answered. */
  async saveProjectCommand(projectId: number, commandType: string, commandText: string, signal?: AbortSignal): Promise<void> {
    await this.request<unknown>(`/api/projects/${projectId}/profile/commands`, {
      method: 'POST',
      body: { projectId, commandType, commandText, isDefault: true, isEnabled: true, timeoutSeconds: 300 },
      signal
    });
  }

  /** Confirms (or edits) the architecture profile — detection proposed it; this records the human's answer. */
  async saveProjectProfile(projectId: number, profile: Record<string, unknown>, signal?: AbortSignal): Promise<void> {
    await this.request<unknown>(`/api/projects/${projectId}/profile`, {
      method: 'POST',
      body: { ...profile, projectId },
      signal
    });
  }

  /**
   * AFFORDANCE-1: probe a planned surface. A real route answers one of three ways —
   * 501 with the refusal envelope (still planned), success (the surface became real and
   * the calling panel is stale), or an error (backend truth unavailable). The probe never
   * invents state: whatever the backend said is what the caller renders.
   */
  async probePlannedSurface(
    path: string,
    method: 'GET' | 'POST' = 'GET',
    signal?: AbortSignal
  ): Promise<PlannedSurfaceProbe> {
    try {
      const data = await this.request<unknown>(path, {
        method,
        ...(method === 'POST' ? { body: {} } : {}),
        signal
      });
      return { kind: 'ready', data };
    } catch (error: unknown) {
      if (error instanceof IronDevApiError) {
        if (error.status === 501 && isPlannedSurfaceEnvelope(error.body)) {
          return { kind: 'notImplemented', envelope: error.body };
        }
        return { kind: 'error', status: error.status, message: error.message };
      }
      return {
        kind: 'error',
        status: null,
        message: error instanceof Error ? error.message : 'The request did not reach IronDev.Api.'
      };
    }
  }

  private async request<T>(path: string, options: RequestOptions): Promise<T> {
    const headers = new Headers({ Accept: 'application/json' });

    if (options.body !== undefined && options.rawBody !== undefined) {
      throw new Error('IronDev API requests cannot contain both JSON and raw bodies.');
    }

    if (options.body !== undefined) {
      headers.set('Content-Type', 'application/json');
    }

    if (!options.skipAuth && this.config.token) {
      headers.set('Authorization', `Bearer ${this.config.token}`);
    }

    const response = await fetch(`${this.config.requestBaseUrl}${path}`, {
      method: options.method,
      headers,
      body: options.rawBody ?? (options.body === undefined ? undefined : JSON.stringify(options.body)),
      signal: options.signal
    });

    if (!response.ok) {
      throw new IronDevApiError(`IronDev.Api request failed with HTTP ${response.status}.`, response.status, await readBody(response));
    }

    if (response.status === 204) {
      return undefined as T;
    }

    const text = await response.text();
    if (text.trim().length === 0) {
      return undefined as T;
    }

    return JSON.parse(text) as T;
  }
}

type RawSkeletonAgentProfile = Omit<SkeletonAgentProfile, 'role' | 'provider'> & {
  role: string | number;
  provider: string | null;
};

const skeletonAgentRoleNames = new Map<number, string>([
  [0, 'Orchestrator'],
  [1, 'Builder'],
  [2, 'Tester'],
  [3, 'Critic']
]);

function normalizeSkeletonAgentProfile(profile: RawSkeletonAgentProfile): SkeletonAgentProfile {
  return {
    ...profile,
    role: normalizeSkeletonAgentRole(profile.role),
    provider: (profile.provider ?? '').toLowerCase()
  };
}

function normalizeSkeletonAgentRole(role: string | number): string {
  if (typeof role === 'number') {
    return skeletonAgentRoleNames.get(role) ?? `Role${role}`;
  }

  const trimmed = role.trim();
  const numeric = Number.parseInt(trimmed, 10);
  if (/^\d+$/.test(trimmed) && Number.isFinite(numeric)) {
    return skeletonAgentRoleNames.get(numeric) ?? `Role${trimmed}`;
  }

  return trimmed;
}

interface RequestOptions {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  rawBody?: BodyInit;
  signal?: AbortSignal;
  skipAuth?: boolean;
}

/** AFFORDANCE-1: the three honest outcomes of probing a planned surface. */
export type PlannedSurfaceProbe =
  | { kind: 'notImplemented'; envelope: PlannedSurfaceEnvelope }
  | { kind: 'ready'; data: unknown }
  | { kind: 'error'; status: number | null; message: string };

function isPlannedSurfaceEnvelope(body: unknown): body is PlannedSurfaceEnvelope {
  if (body === null || typeof body !== 'object') {
    return false;
  }
  const candidate = body as Partial<PlannedSurfaceEnvelope>;
  return (
    candidate.reason === 'NotImplemented' &&
    typeof candidate.surface === 'string' &&
    typeof candidate.plannedSlice === 'string' &&
    typeof candidate.nextSafeAction === 'string'
  );
}

/** Deterministic governance-scope Guid for an int project id — mirrors TicketSkeletonRunService.ApprovalProjectGuid. */
export function approvalProjectGuidFor(projectId: number): string {
  return `${String(projectId).padStart(8, '0')}-0000-0000-0000-000000000000`;
}

function parseOptionalInt(value: string | null) {
  if (!value) {
    return undefined;
  }

  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function parseFallbackProjectId(value: string | null | undefined): number | null {
  const trimmed = value?.trim();
  if (trimmed && /^(none|disabled|manual)$/i.test(trimmed)) {
    return null;
  }

  const parsed = Number.parseInt(trimmed || `${DEFAULT_PROJECT_ID}`, 10);
  return Number.isFinite(parsed) ? parsed : DEFAULT_PROJECT_ID;
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
