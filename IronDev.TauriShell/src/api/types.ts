import type { components } from './generated/ironDevApiTypes';

export type ApiConnectionStatus = 'loading' | 'connected' | 'disconnected' | 'authRequired' | 'error';

export type ProductAccessStatus =
  | 'loading'
  | 'apiOffline'
  | 'apiError'
  | 'authRequired'
  | 'authInvalid'
  | 'tenantRequired'
  | 'projectRequired'
  | 'loadingTickets'
  | 'ready'
  | 'emptyTickets'
  | 'error';

export interface ApiStatus {
  status: ApiConnectionStatus;
  baseUrl: string;
  message: string;
}

export interface EnvironmentInfo {
  environment: string;
  database: string;
  weaviatePrefix: string;
  isTestEnvironment: boolean;
  workspaceRoot: string;
  logsRoot: string;
  dangerRealRepoWritesEnabled: boolean;
}

export type ProjectTicket = components['schemas']['ProjectTicket'];
export type ProjectSummary = components['schemas']['Project'];
export type ProjectDocument = components['schemas']['ProjectDocument'];
export type ProjectDocumentVersion = components['schemas']['ProjectDocumentVersion'];
export type BuildReadinessResult = components['schemas']['BuildReadinessResult'];
export type CreateProjectTicketRequest = components['schemas']['CreateProjectTicketRequest'];
export type ProjectImplementationPlan = components['schemas']['ProjectImplementationPlan'];
export type ChatCompletionRequest = components['schemas']['ChatCompletionRequest'];
export type ChatClarificationKind =
  | 'None'
  | 'GeneralScope'
  | 'ProductScope'
  | 'MissingProjectContext'
  | 'GovernanceIntent'
  | 'SafetyOrRisk';
export interface ChatClarificationState {
  required: boolean;
  kind: ChatClarificationKind;
  questions: string[];
  reason?: string | null;
}
export interface ChatGovernanceGate {
  mode?: string | null;
  canSaveDiscussion?: boolean | null;
  canCreateTicket?: boolean | null;
  canViewSources?: boolean | null;
  canCopyMarkdown?: boolean | null;
  reason?: string | null;
  confidence?: number | null;
  governanceActions?: string[] | null;
}
export type ChatAuditSource = 'durable' | 'tags' | 'live' | 'none';
export interface ChatTurnAuditResponse {
  chatMessageId: number;
  source: 'DurableAudit';
  mode: string;
  modeConfidence: number;
  modeReason: string;
  clarification: ChatClarificationState;
  gate: ChatGovernanceGate;
  routeTraceId?: string | null;
  dogfoodTraceId?: string | null;
  contextSummary?: string | null;
  linkedFilePaths?: string | null;
  linkedSymbols?: string | null;
  isFallbackEvidence: boolean;
}
export type ChatCompletionResponse = components['schemas']['ChatCompletionResponse'] & {
  mode?: string | null;
  modeConfidence?: number | null;
  modeReason?: string | null;
  clarification?: ChatClarificationState | null;
  gate?: ChatGovernanceGate | null;
  reasoningTrace?: string[] | null;
  disambiguationQuestion?: string | null;
  reasoningSummary?: string | null;
  dogfoodTraceId?: string | null;
  dogfoodTracePath?: string | null;
  routeTraceId?: string | null;
  auditSource?: ChatAuditSource;
  auditFallbackReason?: string | null;
  auditHasFallbackEvidence?: boolean;
};
export type ProjectChatSession = components['schemas']['ProjectChatSession'];
export type ChatMessage = components['schemas']['ChatMessage'];

export interface SaveProjectChatSessionRequest {
  id?: number;
  projectId: number;
  title?: string;
  summary?: string | null;
}

export interface SaveProjectChatMessageRequest {
  projectId: number;
  chatSessionId: number;
  role: 'user' | 'assistant';
  message: string;
  tags?: string | null;
  contextSummary?: string | null;
  linkedFilePaths?: string | null;
  linkedSymbols?: string | null;
}

export type TicketDetailLoadStatus = 'idle' | 'loading' | 'loaded' | 'error';
export type TicketReadinessLoadStatus = 'idle' | 'loading' | 'loaded' | 'unavailable' | 'error';
export type TicketCreateStatus = 'idle' | 'validating' | 'submitting' | 'success' | 'error';
export type TicketSaveStatus = 'idle' | 'editing' | 'dirty' | 'saving' | 'saved' | 'error' | 'validation';
export type TicketPlanStatus = 'idle' | 'loading' | 'loaded' | 'unavailable' | 'error';
export type RunReportsLoadStatus = 'idle' | 'loading' | 'loaded' | 'error' | 'unavailable';
export type TicketEvidenceLoadStatus = 'idle' | 'loading' | 'loaded' | 'unavailable' | 'error';
export type LinkedRunStatus =
  | 'passed'
  | 'failed'
  | 'needsHumanReview'
  | 'blocked'
  | 'running'
  | 'unknown';

export interface LinkedRunSummary {
  runId: string;
  traceId?: string | null;
  title?: string | null;
  status: LinkedRunStatus;
  recommendation?: string | null;
  startedUtc?: string | null;
  completedUtc?: string | null;
}

export interface LinkedPromotionPackageSummary {
  packageId?: string | null;
  proposedChangeId?: string | null;
  approvalState?: string | null;
  recommendation?: string | null;
  runtimeProfile?: string | null;
  targetLanguage?: string | null;
  filesToPromoteCount?: number | null;
  filesBlockedCount?: number | null;
  activeRepoMutationCount?: number | null;
  sourceRunId?: string | null;
}

export interface TicketEvidenceSummary {
  ticketId: number;
  status: TicketEvidenceLoadStatus;
  message: string;
  latestRun?: LinkedRunSummary | null;
  latestPromotionPackage?: LinkedPromotionPackageSummary | null;
  linkedTraceCount: number;
  linkedDocumentCount: number;
  linkedDecisionCount: number;
  linkedRunCount: number;
  hasBlockingWarnings: boolean;
  blockedActions: string[];
  nextSafeAction?: string | null;
}

export interface StartTicketBuildRunRequest {
  workflowRunId?: string | null;
  maxRetries?: number | null;
}

export interface TicketBuildRunDto {
  runId: string;
  projectId: number;
  ticketId: number;
  status: string;
  currentNode: string;
  requiresHumanApproval: boolean;
  message?: string | null;
}

export interface SaveDiscussionRequest {
  title: string;
  content: string;
}

export interface SaveDiscussionResponse {
  documentId: number;
  documentVersionId: number;
}

export interface CreateTicketFromDocumentRequest {
  requestedTitle?: string | null;
}

export interface CreateTicketFromDocumentResponse {
  ticketId: number;
  sourceDocumentVersionId: number;
}

export interface RunTicketReviewRequest {
  useLiveModel: boolean;
}

export interface TicketReviewContribution {
  role: string;
  summary: string;
  concerns: string[];
  recommendations: string[];
}

export interface TicketReviewDecision {
  proceed: boolean;
  recommendedNextStep: string;
  guardrails: string[];
}

export interface TicketReviewResult {
  reviewId: string;
  projectId: number;
  ticketId: number;
  scenarioId: string;
  contributions: TicketReviewContribution[];
  decision: TicketReviewDecision;
  createdUtc: string;
}

export interface RunTicketReviewResponse {
  reviewId: string;
  result: TicketReviewResult;
}

export interface StartDisposableCodeRunRequest {
  reviewId: string;
  scenarioId?: string;
  expectedOutput?: string;
}

export interface StartDisposableCodeRunResponse {
  runId: string;
  state: string;
  isDisposable: boolean;
}

export interface GeneratedCodeFile {
  relativePath: string;
  content: string;
  sha256: string;
}

export interface CommandEvidence {
  command: string;
  exitCode?: string | null;
  stdoutPath?: string | null;
  stderrPath?: string | null;
  durationMs?: string | null;
}

export interface OutputVerificationEvidence {
  expected: string;
  actual: string;
  verified: boolean;
  evidencePath?: string | null;
}

export interface CodeStandardsEvidence {
  status: string;
  summary: string;
  evidencePath?: string | null;
}

export interface RunEventSummary {
  eventType: string;
  message: string;
  timestampUtc: string;
}

export interface RunReviewPackage {
  runId: string;
  projectId: number;
  ticketId: number;
  state: string;
  generatedFiles: GeneratedCodeFile[];
  commandEvidence: CommandEvidence[];
  outputVerification: OutputVerificationEvidence;
  outputVerifications: OutputVerificationEvidence[];
  codeStandards: CodeStandardsEvidence;
  fileSetHash: string;
  risks: string[];
  humanReviewChecklist: string[];
  events: RunEventSummary[];
}

export interface RunEventDto {
  eventId?: string | null;
  timestampUtc?: string | null;
  runId?: string | null;
  eventType?: string | null;
  message?: string | null;
  payload?: Record<string, string> | null;
}

export interface TicketRunReview {
  runId: string;
  projectId: number;
  ticketId: number;
  ticketTitle: string;
  status: string;
  startedUtc?: string | null;
  completedUtc?: string | null;
  isDisposableRun: boolean;
  traceId?: string | null;
  evidenceSummary: string;
  outputSummary: string;
  failureReason?: string | null;
  reportPath?: string | null;
  tracePath?: string | null;
  logPath?: string | null;
  evidence: RunEvidenceItem[];
  events: RunEventDto[];
}

export interface RunReportSummaryRow {
  runId?: string | null;
  traceId?: string | null;
  project?: string | null;
  title?: string | null;
  status?: string | null;
  recommendation?: string | null;
  startedUtc?: string | null;
  completedUtc?: string | null;
  realRepoMutationCount?: number | null;
  disposableFilesChanged?: number | null;
}

export type RunEvidenceItem = components['schemas']['RunEvidenceItem'];
export type RunReportDetail = components['schemas']['RunReportDetail'];
export type RunReportSummary = components['schemas']['RunReportSummary'];
export type RunPromotionReview = components['schemas']['RunPromotionReview'];
export type RunReviewPolicySnapshot = components['schemas']['RunReviewPolicySnapshot'];

export interface GovernanceTraceQuery {
  projectReferenceId?: string;
  workflowRunId?: string;
  workflowStepId?: string;
  correlationId?: string;
  causationId?: string;
  subjectReferenceId?: string;
  eventKind?: string;
  sourceComponent?: string;
  fromUtc?: string;
  toUtc?: string;
  take?: number;
}

export interface GovernanceTraceIssue {
  code?: string | null;
  field?: string | null;
  message?: string | null;
}

export interface GovernanceTraceSummary {
  traceId?: string | null;
  projectReferenceId?: string | null;
  workflowRunId?: string | null;
  workflowStepId?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
  subjectReferenceId?: string | null;
  eventKind?: string | null;
  sourceComponent?: string | null;
  safeSummary?: string | null;
  recordedUtc?: string | null;
  isReadOnlyTrace?: boolean | null;
  isAuthorityDecision?: boolean | null;
  isApproval?: boolean | null;
  isPolicySatisfaction?: boolean | null;
  isWorkflowTransition?: boolean | null;
  canApprove?: boolean | null;
  canReject?: boolean | null;
  canSatisfyPolicy?: boolean | null;
  canTransitionWorkflow?: boolean | null;
  canInvokeTool?: boolean | null;
  canDispatchAgent?: boolean | null;
  canCallModel?: boolean | null;
  canPromoteMemory?: boolean | null;
  canApplySource?: boolean | null;
}

export interface GovernanceTraceTimelineItem {
  eventId?: string | null;
  eventKind?: string | null;
  sourceComponent?: string | null;
  safeSummary?: string | null;
  recordedUtc?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
  subjectReferenceId?: string | null;
}

export interface GovernanceTraceRelatedReference {
  referenceKind?: string | null;
  referenceId?: string | null;
  safeSummary?: string | null;
}

export interface GovernanceTraceDetail {
  summary?: GovernanceTraceSummary | null;
  timeline?: GovernanceTraceTimelineItem[] | null;
  relatedReferences?: GovernanceTraceRelatedReference[] | null;
  boundaryWarnings?: string[] | null;
}

export interface GovernanceTraceListData {
  status?: string | number | null;
  traces?: GovernanceTraceSummary[] | null;
  issues?: GovernanceTraceIssue[] | null;
  boundaryWarnings?: string[] | null;
}

export interface GovernanceTraceDetailData {
  status?: string | number | null;
  trace?: GovernanceTraceDetail | null;
  issues?: GovernanceTraceIssue[] | null;
  boundaryWarnings?: string[] | null;
}

export interface GovernanceTraceApiBoundary {
  readOnlyTrace?: boolean | null;
  traceabilityIsAuthority?: boolean | null;
  traceOutputIsApproval?: boolean | null;
  traceOutputIsPolicySatisfaction?: boolean | null;
  traceOutputIsWorkflowTransition?: boolean | null;
  traceOutputIsToolInvocation?: boolean | null;
  traceOutputIsAgentDispatch?: boolean | null;
  traceOutputIsModelExecution?: boolean | null;
  traceOutputIsMemoryPromotion?: boolean | null;
  traceOutputIsSourceApply?: boolean | null;
  traceOutputIsPatchApply?: boolean | null;
  exposesRawPayloadJson?: boolean | null;
  exposesPrivateReasoning?: boolean | null;
}

export interface GovernanceTraceApiEnvelope<TData> {
  status?: string | null;
  mutationOccurred?: boolean | null;
  boundary?: GovernanceTraceApiBoundary | null;
  warnings?: string[] | null;
  errors?: GovernanceTraceIssue[] | null;
  data?: TData | null;
}

export interface ToolGateFilter {
  projectReferenceId?: string;
  workflowRunId?: string;
  workflowStepId?: string;
  toolRequestId?: string;
  gateDecisionId?: string;
  correlationId?: string;
  decisionStatus?: string;
  toolName?: string;
  sourceComponent?: string;
  fromUtc?: string;
  toUtc?: string;
  take?: number;
}

export interface ToolGateIssue {
  code?: string | null;
  field?: string | null;
  message?: string | null;
}

export interface ToolRequestListItem {
  toolRequestId: string;
  projectReferenceId?: string | null;
  workflowRunId?: string | null;
  workflowStepId?: string | null;
  correlationId?: string | null;
  requestedToolName: string;
  requestedCapability?: string | null;
  requestedOperation?: string | null;
  requestStatus: string;
  sourceComponent?: string | null;
  createdUtc?: string | null;
  subjectReference?: string | null;
  safeSummary: string;
}

export interface ToolGateDecisionListItem {
  decisionId: string;
  toolRequestId: string;
  decisionStatus: string;
  policyOutcomeSummary: string;
  approvalRequirementSummary: string;
  safeReason: string;
  decidedUtc?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
  subjectReference?: string | null;
  safeSummary: string;
}

export interface ToolGateApiBoundary {
  readOnly?: boolean | null;
  durable?: boolean | null;
  mutationOccurred?: boolean | null;
  requestVisibilityIsExecutionPermission?: boolean | null;
  gateDecisionVisibilityIsAuthority?: boolean | null;
  approvalRequirementIsApproval?: boolean | null;
  policyEvidenceIsPolicySatisfaction?: boolean | null;
  gateStatusIsToolInvocation?: boolean | null;
  canApprove?: boolean | null;
  canReject?: boolean | null;
  canOverrideGate?: boolean | null;
  canReopenGate?: boolean | null;
  canSatisfyPolicy?: boolean | null;
  canExecuteTool?: boolean | null;
  canInvokeTool?: boolean | null;
  canDispatchAgent?: boolean | null;
  canTransitionWorkflow?: boolean | null;
  canApplySource?: boolean | null;
  canApplyPatch?: boolean | null;
}

export interface ToolGateApiEnvelope<TData> {
  status?: string | null;
  mutationOccurred?: boolean | null;
  durable?: boolean | null;
  boundary?: ToolGateApiBoundary | null;
  warnings?: string[] | null;
  errors?: ToolGateIssue[] | null;
  data?: TData | null;
}

export type ToolRequestDetailData = Record<string, unknown>;
export type ToolGateDecisionDetailData = Record<string, unknown>;

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  userId: number;
  displayName: string;
}

export interface UserProfile {
  userId: number;
  email: string;
  displayName: string;
  selectedTenantId: number | null;
}

export interface TenantSummary {
  id: number;
  name: string;
  slug: string;
}

export interface TicketLoadResult {
  tickets: ProjectTicket[];
  status: ApiConnectionStatus;
  message: string;
}

export interface ProjectContextState {
  tenants: TenantSummary[];
  projects: ProjectSummary[];
  selectedTenantId: number | null;
  selectedProjectId: number | null;
  selectedProjectName: string | null;
  projectSelectionMode: 'api' | 'fallback-config' | 'missing';
}
