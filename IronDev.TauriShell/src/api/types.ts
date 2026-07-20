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
  workbench: WorkbenchReleaseInfo;
}

export interface WorkbenchReleaseInfo {
  version: string;
  mode: 'V1' | 'V2';
  v2Enabled: boolean;
  v1FallbackEnabled: boolean;
  conversationAuthorityEnabled?: boolean;
  previewId: string;
  apiBuildIdentity: string;
  apiCommit: string;
  resetSupported: boolean;
}

export type LocalTestPreflightState =
  | 'ApiOffline'
  | 'ApiConnected'
  | 'WrongEnvironment'
  | 'WrongDatabase'
  | 'SeedUserMissing'
  | 'SeedCredentialInvalid'
  | 'SeedMembershipMissing'
  | 'ApiIdentityMismatch'
  | 'SessionCapabilityMismatch'
  | 'DatabaseUnavailable'
  | 'LocalTestReady';

export interface LocalTestPreflightInfo {
  state: LocalTestPreflightState;
  environment: string;
  database: string | null;
  apiBuildIdentity: string;
  apiBuildCommit: string;
  launcherRepositoryCommit: string | null;
  sessionId: string | null;
  apiBaseUrl: string | null;
  apiPid: number;
  seedContractVersion: number | null;
  seededLoginCheckResult: string;
  nextSafeAction: string;
  resetCommand: string | null;
  detail: string;
  workbenchVersion: string;
  workbenchMode: 'V1' | 'V2';
  previewId: string;
  sessionMode: string;
  sandboxApplyRequested: boolean;
  sandboxApplyEnabled: boolean;
  sandboxApplyRoot: string | null;
  capabilities: string[];
  sandboxApplyRestartCommand: string;
}

export type ProjectTicket = components['schemas']['ProjectTicket'] & { revision?: number };
export type ProjectSummary = components['schemas']['Project'] & {
  lifecyclePhase?: string | null;
  executionReadiness?: string | null;
};
export interface WorkbenchProjectEntryContext {
  projectId: number;
  tenantId: number;
  name: string;
  projectLifecyclePhase: 'Shaping' | 'Delivery' | 'Archived' | string;
  executionReadiness: 'NotConfigured' | 'ValidationRequired' | 'Ready' | string;
  repositoryBinding: null;
  workbenchSessionId: number;
  leaseEpoch: number;
  wasResumed: boolean;
  wasTakenOver: boolean;
  clientOperationId: string;
}

export type ProjectUnderstandingFactState = 'Unknown' | 'Inferred' | 'Confirmed' | 'Conflicted';

export interface ProjectUnderstandingFact {
  key: string;
  value: string;
  state: ProjectUnderstandingFactState | string;
  userLocked: boolean;
  authorKind: string;
  authorActorUserId: number | null;
  authorAgentRunId: string | null;
  sourceMessageIds: number[];
  evidenceSummary: string;
  revision: number;
}

export interface ProjectUnderstandingConflict {
  conflictId: string;
  factKey: string;
  currentValue: string;
  proposedValue: string;
  sourceMessageIds: number[];
  evidenceSummary: string;
  createdByAgentRunId: string;
  createdAtRevision: number;
  status: string;
  resolvedAtRevision: number | null;
  resolvedByActorUserId: number | null;
}

export interface ProjectRenameProposal {
  proposalId: string;
  proposedName: string;
  status: string;
  basedOnProjectName: string;
  basedOnUnderstandingRevision: number;
  proposedByAgentRunId: string;
  initiatingActorUserId: number;
  sourceMessageIds: number[];
  evidenceSummary: string;
  createdAtUtc: string;
}

export interface ProjectUnderstandingOperationalProjection {
  projectLifecyclePhase: string;
  projectLifecycleAuthority: 'ProjectLifecyclePhase' | string;
  executionReadiness: string;
  executionReadinessAuthority: 'ProjectReadinessAssessment' | string;
  repositoryBinding: unknown | null;
}

export interface ProjectUnderstandingReadModel {
  projectId: number;
  tenantId: number;
  projectName: string;
  revision: number;
  facts: ProjectUnderstandingFact[];
  conflicts: ProjectUnderstandingConflict[];
  openQuestions: string[];
  pendingRenameProposal: ProjectRenameProposal | null;
  operationalProjections: ProjectUnderstandingOperationalProjection;
}

export type ProjectUnderstandingFactAction = 'Edit' | 'Confirm' | 'SetLock' | 'ResolveConflict';

export interface UpdateProjectUnderstandingFactRequest {
  workbenchSessionId: number;
  leaseEpoch: number;
  expectedUnderstandingRevision: number;
  clientOperationId: string;
  action: ProjectUnderstandingFactAction;
  conflictId?: string;
  value?: string;
  userLocked?: boolean;
}

export interface ProjectUnderstandingMutationResult {
  snapshot: ProjectUnderstandingReadModel;
  clientOperationId: string;
  isReplay: boolean;
}

export interface AcceptProjectRenameProposalRequest {
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
}

export interface AcceptProjectRenameProposalResult {
  snapshot: ProjectUnderstandingReadModel;
  clientOperationId: string;
  isReplay: boolean;
}
export interface StartProjectResponse {
  projectId: number;
  tenantId: number;
  name: string;
  projectLifecyclePhase: 'Shaping' | string;
  executionReadiness: 'NotConfigured' | string;
  repositoryBinding: null;
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
  createdAtUtc: string;
  isReplay: boolean;
}
export type ProjectGovernanceOverview = components['schemas']['ProjectGovernanceOverview'];
export type ProjectGovernanceAttentionItem = components['schemas']['ProjectGovernanceAttentionItem'];
export type ProjectGovernanceControl = components['schemas']['ProjectGovernanceControl'];
export type ProjectGovernanceException = components['schemas']['ProjectGovernanceException'];
export type ProjectGovernanceDecision = components['schemas']['ProjectGovernanceDecision'];
export type ProjectAuditExport = components['schemas']['ProjectAuditExport'];
export type ProjectAuditExportFilters = components['schemas']['ProjectAuditExportFilters'];
export type ProjectBoardReadModel = components['schemas']['ProjectBoardReadModel'];
export type ProjectBoardItemReadModel = components['schemas']['ProjectBoardItemReadModel'];
export type ProjectWorkItemReadModel = components['schemas']['ProjectWorkItemReadModel'];
export type ProjectDocument = components['schemas']['ProjectDocument'] & {
  origin?: string | null;
  processingStatus?: string | null;
  processingFailureReason?: string | null;
  processingStartedAtUtc?: string | null;
  processingCompletedAtUtc?: string | null;
  description?: string | null;
  visibility?: string | null;
  originalFileName?: string | null;
  mediaType?: string | null;
  byteSize?: number | null;
};
export type ProjectDocumentVersion = components['schemas']['ProjectDocumentVersion'];
export interface ProjectDocumentUploadResult {
  document: ProjectDocument;
  version: ProjectDocumentVersion;
  processingStatus: string;
  boundary: string;
}
export interface ProjectDocumentProcessingResult {
  document: ProjectDocument;
  version: ProjectDocumentVersion;
  contextDocumentId?: number | null;
  succeeded: boolean;
  status: string;
  failureReason?: string | null;
  nextSafeAction: string;
  boundary: string;
}
export interface ProjectToolCatalogueResponse {
  projectId: number;
  projectName: string;
  tools: ProjectToolSummary[];
  boundary: string;
}
export interface ProjectToolSummary {
  toolId: string;
  displayName: string;
  category: string;
  description: string;
  registrationStatus: string;
  connectionStatus: string;
  projectUseStatus: string;
  directInvocationStatus: string;
  healthStatus: string;
  effectiveScopeSummary: string;
  boundary: string;
}
export interface ProjectToolCapabilities {
  mutatesState: boolean;
  allowsNestedCalls: boolean;
  allowsFileWrites: boolean;
  allowsProcessExecution: boolean;
  allowsNetworkAccess: boolean;
  allowsWorkspaceMutation: boolean;
}
export interface ProjectToolDetailResponse extends ProjectToolSummary {
  projectId: number;
  projectName: string;
  definitionVersion: string;
  capabilities: ProjectToolCapabilities;
  inputContract: string;
  outputContract: string;
  allowedCallers: string[];
  evidenceKinds: string[];
}
export type BuildReadinessResult = components['schemas']['BuildReadinessResult'];
export type CreateProjectTicketRequest = components['schemas']['CreateProjectTicketRequest'];
export type ProjectImplementationPlan = components['schemas']['ProjectImplementationPlan'];
export type ChatCompletionRequest = components['schemas']['ChatCompletionRequest'] & {
  sourceMessageId?: number | null;
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
};
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
export interface ChatRouteChallenge {
  suggestedMode?: string | null;
  suggestedRequestKind?: string | null;
  confidence?: number | null;
  reason?: string | null;
}
export interface BaWorkingDraft {
  candidateTitle?: string | null;
  problem?: string | null;
  proposedChange?: string | null;
  businessRules?: string[] | null;
  acceptanceCriteria?: string[] | null;
  assumptions?: string[] | null;
  openQuestions?: string[] | null;
  sourceMessageIds?: string[] | null;
  confidence?: number | null;
  readyForConfirmation?: boolean | null;
  potentialConflicts?: string[] | null;
  suggestedArtifact?: string | null;
  boundary?: string | null;
}
export type ConfirmBaWorkingDraftRequest = Omit<
  components['schemas']['ConfirmBaWorkingDraftRequest'],
  'draft'
> & {
  draft: BaWorkingDraft;
};
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
  routeSource?: string | null;
  routeChallenge?: ChatRouteChallenge | null;
  baDraft?: BaWorkingDraft | null;
}
interface ChatCompletionResponseOverrides {
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
  routeSource?: string | null;
  routeChallenge?: ChatRouteChallenge | null;
  baDraft?: BaWorkingDraft | null;
  auditSource?: ChatAuditSource;
  auditFallbackReason?: string | null;
  auditHasFallbackEvidence?: boolean;
  documentSources?: ChatDocumentSource[] | null;
}
export type ChatCompletionResponse = Omit<
  components['schemas']['ChatCompletionResponse'],
  keyof ChatCompletionResponseOverrides
> & ChatCompletionResponseOverrides;
export type ProjectChatSession = components['schemas']['ProjectChatSession'];
export interface ChatDocumentSource {
  documentId: number;
  documentVersionId: number;
  title: string;
  documentType: string;
  versionLabel: string;
  status: string;
  boundary: string;
}
interface ChatMessageOverrides {
  replyToMessageId?: number | null;
  documentSources?: ChatDocumentSource[] | null;
}
export type ChatMessage = Omit<components['schemas']['ChatMessage'], keyof ChatMessageOverrides> & ChatMessageOverrides;

export type SaveProjectChatSessionRequest = Omit<
  components['schemas']['SaveProjectChatSessionRequest'],
  'projectId'
> & {
  id?: number;
  projectId: number;
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
};

export type SaveProjectChatMessageRequest = Omit<
  components['schemas']['SaveProjectChatMessageRequest'],
  'projectId' | 'chatSessionId' | 'role' | 'message'
> & {
  projectId: number;
  chatSessionId: number;
  role: 'user' | 'assistant';
  message: string;
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
};

export type WorkbenchAgentRunStatus =
  | 'Pending'
  | 'Running'
  | 'NeedsInput'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'Superseded'
  | 'Stale';

export interface SubmitWorkbenchAgentRunRequest {
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
  chatSessionId: number;
  message: string;
}

export interface SubmitWorkbenchAgentRunResult {
  agentRunId: string;
  projectId: number;
  workbenchSessionId: number;
  leaseEpoch: number;
  chatSessionId: number;
  userMessageId: number;
  status: WorkbenchAgentRunStatus;
  clientOperationId: string;
  createdAtUtc: string;
  isReplay: boolean;
}

export interface SubmitWorkbenchInputRequest {
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
  chatSessionId: number | null;
  composerText: string;
}

export interface SubmitWorkbenchCommandResult {
  kind: 'Help' | 'Ticket';
  projectId: number;
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
  normalizedCommand: '/help' | '/ticket';
  instruction: string | null;
  title: string;
  message: string;
  isReplay: boolean;
  agentRun: null;
  rawCommandToken: null;
  reasonCode: null;
}

export interface SubmitWorkbenchAgentInputResult {
  kind: 'AgentRun';
  projectId: number;
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
  normalizedCommand: null;
  instruction: null;
  title: null;
  message: null;
  isReplay: boolean;
  agentRun: SubmitWorkbenchAgentRunResult;
  rawCommandToken: null;
  reasonCode: null;
}

export type SubmitWorkbenchInputResult = SubmitWorkbenchCommandResult | SubmitWorkbenchAgentInputResult;

export interface WorkbenchAgentRunSnapshot {
  agentRunId: string;
  tenantId: number;
  projectId: number;
  workbenchSessionId: number;
  leaseEpoch: number;
  actorUserId: number;
  chatSessionId: number;
  sourceUserMessageId: number;
  status: WorkbenchAgentRunStatus;
  attemptCount: number;
  assistantMessageId: number | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  cancellationRequestedAtUtc: string | null;
  failureCategory: string | null;
  retryable: boolean;
}

export interface CurrentWorkbenchAgentRunResponse {
  submissionAvailable: boolean;
  unavailableCategory: string | null;
  boundChatSessionId: number | null;
  activeRun: WorkbenchAgentRunSnapshot | null;
  latestRun: WorkbenchAgentRunSnapshot | null;
}

export interface CancelWorkbenchAgentRunRequest {
  workbenchSessionId: number;
  leaseEpoch: number;
  clientOperationId: string;
}

export interface CancelWorkbenchAgentRunResult {
  agentRunId: string;
  status: WorkbenchAgentRunStatus;
  cancellationRequested: boolean;
  clientOperationId: string;
  isReplay: boolean;
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

export type StartTicketBuildRunRequest = components['schemas']['StartTicketBuildRunRequest'];

export interface TicketBuildRunDto {
  runId: string;
  projectId: number;
  ticketId: number;
  status: string;
  currentNode: string;
  requiresHumanApproval: boolean;
  message?: string | null;
}

export type SaveDiscussionRequest = Omit<components['schemas']['SaveDiscussionRequest'], 'title' | 'content'> & {
  title: string;
  content: string;
};

export interface SaveDiscussionResponse {
  documentId: number;
  documentVersionId: number;
}

export type CreateTicketFromDocumentRequest = components['schemas']['CreateTicketFromDocumentRequest'];

export interface CreateTicketFromDocumentResponse {
  ticketId: number;
  sourceDocumentVersionId: number;
}

export type RunTicketReviewRequest = components['schemas']['RunTicketReviewRequest'];

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

export type StartDisposableCodeRunRequest = Omit<components['schemas']['StartDisposableCodeRunRequest'], 'reviewId'> & {
  reviewId: string;
};

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

export interface AuditLedgerQuery {
  projectId?: number;
  workItemId?: number;
  actor?: string;
  event?: string;
  fromUtc?: string;
  toUtc?: string;
  take?: number;
}

export interface AuditLedgerBoundary {
  readOnly?: boolean | null;
  grantsAuthority?: boolean | null;
  canApprove?: boolean | null;
  canContinueWorkflow?: boolean | null;
  canApplySource?: boolean | null;
  exposesRawPayloadJson?: boolean | null;
  boundaryStatement?: string | null;
}

export interface AuditLedgerIssue {
  code?: string | null;
  field?: string | null;
  message?: string | null;
}

export interface AuditLedgerEvidenceLink {
  label?: string | null;
  href?: string | null;
}

export interface AuditLedgerItem {
  ledgerId?: string | null;
  timeUtc?: string | null;
  projectId?: number | null;
  projectName?: string | null;
  workItemId?: number | null;
  workItemTitle?: string | null;
  source?: string | null;
  actorId?: string | null;
  actorDisplayName?: string | null;
  action?: string | null;
  outcome?: string | null;
  summary?: string | null;
  correlationId?: string | null;
  evidenceLinks?: AuditLedgerEvidenceLink[] | null;
}

export interface AuditLedgerResponse {
  status?: string | null;
  boundary?: AuditLedgerBoundary | null;
  warnings?: string[] | null;
  issues?: AuditLedgerIssue[] | null;
  items?: AuditLedgerItem[] | null;
  returnedCount?: number | null;
  take?: number | null;
}

export interface DogfoodLoopIssue {
  category?: string | null;
  code?: string | null;
  field?: string | null;
  message?: string | null;
}

export interface DogfoodLoopReferenceData {
  refType?: string | null;
  refId?: string | null;
  summary?: string | null;
  durable?: boolean | null;
  backendRecorded?: boolean | null;
  source?: string | null;
}

export interface DogfoodReceiptDetailData {
  dogfoodLoopId?: string | null;
  runId?: string | null;
  receiptId?: string | null;
  evidenceId?: string | null;
  projectId?: string | number | null;
  summary?: string | null;
  goal?: string | null;
  observations?: string[] | null;
  blockedReasons?: string[] | null;
  referencedAgentRuns?: DogfoodLoopReferenceData[] | null;
  referencedCriticReviews?: DogfoodLoopReferenceData[] | null;
  referencedMemoryImprovements?: DogfoodLoopReferenceData[] | null;
  referencedToolRequests?: DogfoodLoopReferenceData[] | null;
  referencedGateDecisions?: DogfoodLoopReferenceData[] | null;
  evidenceRefs?: DogfoodLoopReferenceData[] | null;
  durable?: boolean | null;
  containsNonDurableReferences?: boolean | null;
  durabilityWarnings?: string[] | null;
  knownLimitations?: string[] | null;
  createdAtUtc?: string | null;
  warnings?: string[] | null;
}

export interface DogfoodLoopApiBoundary {
  dogfoodReceiptIsReleaseApproval?: boolean | null;
  dogfoodLoopIsAutonomousWorkflow?: boolean | null;
  toolExecuted?: boolean | null;
  requestApproved?: boolean | null;
  gateExecuted?: boolean | null;
  gateIsExecutor?: boolean | null;
  sourceApplied?: boolean | null;
  memoryPromoted?: boolean | null;
  collectiveMemoryWritten?: boolean | null;
  vectorAuthorityWritten?: boolean | null;
  auditIsApproval?: boolean | null;
  modelOutputIsAuthority?: boolean | null;
  endpointAccessIsExecutionPermission?: boolean | null;
  apiResponseStatusIsGovernance?: boolean | null;
  durable?: boolean | null;
  containsNonDurableReferences?: boolean | null;
  humanReviewRequiredForSourceApply?: boolean | null;
  humanReviewRequiredForMemoryPromotion?: boolean | null;
}

export interface DogfoodLoopApiEnvelope<TData> {
  status?: string | null;
  data?: TData | null;
  dogfoodLoopId?: string | null;
  runId?: string | null;
  receiptId?: string | null;
  evidenceId?: string | null;
  boundary?: DogfoodLoopApiBoundary | null;
  mutationOccurred?: boolean | null;
  humanApprovalRequired?: boolean | null;
  warnings?: string[] | null;
  errors?: DogfoodLoopIssue[] | null;
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

export interface ApprovalPackageFilter {
  projectReferenceId?: string;
  workflowRunId?: string;
  workflowStepId?: string;
  approvalPackageId?: string;
  correlationId?: string;
  scope?: string;
  packageStatus?: string;
  sourceComponent?: string;
  fromUtc?: string;
  toUtc?: string;
  take?: number;
}

export interface ApprovalPackageEvidenceReference {
  referenceKind: string;
  referenceId: string;
  safeSummary: string;
}

export interface ApprovalPackageListItem {
  approvalPackageId: string;
  projectReferenceId?: string | null;
  workflowRunId?: string | null;
  workflowStepId?: string | null;
  correlationId?: string | null;
  traceId?: string | null;
  requestedDecision: string;
  approvalScope: string;
  packageStatus: string;
  sourceComponent?: string | null;
  createdUtc?: string | null;
  safeSummary: string;
}

export interface ApprovalPackageDetail {
  approvalPackageId: string;
  requestedDecision: string;
  approvalScope: string;
  packageStatus: string;
  safeSummary: string;
  safeRiskSummary: string;
  safeEvidenceSummary: string;
  evidenceReferences: ApprovalPackageEvidenceReference[];
  missingEvidenceWarnings: string[];
  boundaryWarnings: string[];
}

export interface ApprovalPackageReviewViewModel {
  isReadOnly: true;
  mutationOccurred: false;
  canApprove: false;
  canReject: false;
  canAcceptApproval: false;
  canCreateAcceptedApprovalRecord: false;
  canSatisfyPolicy: false;
  canTransitionWorkflow: false;
  canContinueWorkflow: false;
  canInvokeTool: false;
  canDispatchAgent: false;
  canCallModel: false;
  canApplySource: false;
  canApplyPatch: false;
  canApproveRelease: false;
  packages: ApprovalPackageListItem[];
  selectedPackage?: ApprovalPackageDetail;
  warnings: string[];
  errors: string[];
}

export type LoginRequest = Omit<components['schemas']['LoginRequest'], 'email' | 'password'> & {
  email: string;
  password: string;
};

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

export interface TenantUser {
  id: number;
  email: string;
  displayName: string;
  role: string;
  isActive: boolean;
}

export interface ProjectMemberDirectoryEntry {
  userId: number;
  displayName: string;
  email: string;
  tenantRole: string;
  projectRole: string | null;
  isProjectMember: boolean;
  isActive: boolean;
  isCurrentUser: boolean;
  projectAccessStatus: string;
  channelMembershipSummary: string;
}

export interface ProjectMemberDirectoryResponse {
  projectId: number;
  projectName: string;
  tenantId: number;
  currentUserTenantRole: string;
  canAdministerTenantMembership: boolean;
  canAdministerProjectMembership: boolean;
  canAdministerChannelMembership: boolean;
  availableTenantRoles: string[];
  availableProjectRoles: string[];
  availableChannelRoles: string[];
  availableNotificationLevels: string[];
  projectMembershipStatus: string;
  channelMembershipStatus: string;
  members: ProjectMemberDirectoryEntry[];
  channels: ProjectChannelDirectoryEntry[];
  boundary: string;
}

export interface SetProjectWorkItemCollaborationRequest {
  expectedRevision: number;
  assigneeUserId: number | null;
  followerUserIds: number[];
  waitingOnUserId: number | null;
  waitingOnKind: string | null;
  waitingOnLabel: string | null;
}

export interface ProjectChannelMembershipEntry {
  userId: number;
  channelRole: string;
  notificationLevel: string;
  revision: number;
}

export interface ProjectChannelDirectoryEntry {
  channelId: number;
  name: string;
  description: string | null;
  channelKind: string;
  visibility: 'Project' | 'MembersOnly';
  memberCount: number;
  members: ProjectChannelMembershipEntry[];
  boundary: string;
}

export type SetProjectChannelMembershipRequest = Omit<
  components['schemas']['SetProjectChannelMembershipRequest'],
  'channelRole' | 'notificationLevel'
> & {
  channelRole: string;
  notificationLevel: string;
  expectedRevision: number;
};

export type ProjectChannelChatSummary = Omit<
  Required<components['schemas']['ProjectChannelChatSummary']>,
  'slug' | 'visibility'
> & {
  slug: string;
  visibility: 'Project' | 'MembersOnly';
};

export type ProjectChannelChatListResponse = Omit<
  Required<components['schemas']['ProjectChannelChatListResponse']>,
  'channels'
> & {
  channels: ProjectChannelChatSummary[];
};

export type ProjectChannelChatMessage = Omit<
  Required<components['schemas']['ProjectChannelChatMessage']>,
  'role' | 'messageFormat' | 'status'
> & {
  role: 'User' | 'Assistant' | 'SystemNotice' | 'EventLink';
  messageFormat: 'PlainText' | 'Markdown';
  status: 'Active' | 'Edited' | 'Deleted';
};

export type ProjectChannelReadState = Required<components['schemas']['ProjectChannelReadState']>;
export type ProjectChannelPresenceState = Required<components['schemas']['ProjectChannelPresenceState']>;
export type ProjectChannelMentionCandidate = Omit<
  Required<components['schemas']['ProjectChannelMentionCandidate']>,
  'displayName' | 'handle'
> & {
  displayName: string;
  handle: string;
};

export type ProjectNotificationSummary = Omit<
  Required<components['schemas']['ProjectNotificationSummary']>,
  'kind' | 'title' | 'body' | 'createdUtc' | 'boundary'
> & {
  kind: 'Mention' | 'ChannelMessage';
  title: string;
  body: string;
  createdUtc: string;
  boundary: string;
};

export interface ProjectNotificationListResponse {
  unreadCount: number;
  notifications: ProjectNotificationSummary[];
  boundary: string;
}

export type ProjectChannelAssistantTurnState = Omit<
  Required<components['schemas']['ProjectChannelAssistantTurnState']>,
  'requestedByDisplayName' | 'prompt' | 'status' | 'createdUtc' | 'boundary'
> & {
  requestedByDisplayName: string;
  prompt: string;
  status: 'Requested' | 'Answered' | 'Failed' | 'Refused';
  createdUtc: string;
  boundary: string;
};

export interface ProjectChannelPostMessageResult {
  message: ProjectChannelChatMessage;
  assistantTurn: ProjectChannelAssistantTurnState | null;
}

export interface ProjectChannelAssistantCompletionResult {
  assistantTurn: ProjectChannelAssistantTurnState;
  responseMessage: ProjectChannelChatMessage | null;
}

export type ProjectChannelChatDetail = Omit<
  Required<components['schemas']['ProjectChannelChatDetail']>,
  'channel' | 'messages' | 'assistantTurns' | 'mentionCandidates' | 'readState' | 'presence'
> & {
  channel: ProjectChannelChatSummary;
  messages: ProjectChannelChatMessage[];
  assistantTurns: ProjectChannelAssistantTurnState[];
  mentionCandidates: ProjectChannelMentionCandidate[];
  readState: ProjectChannelReadState;
  presence: ProjectChannelPresenceState;
};

export type CreateProjectChannelRequest = Omit<
  components['schemas']['CreateProjectChannelRequest'],
  'name' | 'description' | 'visibility'
> & {
  name: string;
  description: string | null;
  visibility: 'Project' | 'MembersOnly';
};

export type CreateTenantUserRequest = Omit<
  components['schemas']['CreateTenantUserRequest'],
  'email' | 'displayName' | 'password' | 'role'
> & {
  email: string;
  displayName: string;
  password: string | null;
  role: string;
};

export interface ProjectFileSummary {
  id: number;
  filePath: string;
  fileExtension: string;
  lastIndexedDate: string;
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

export interface WorkflowReadOnlyIssue {
  code: string;
  message: string;
  severity: string;
}

export interface WorkflowAuthorityFlagsData {
  createsApproval?: boolean;
  satisfiesApproval?: boolean;
  grantsExecutionPermission?: boolean;
  transitionsWorkflow?: boolean;
  invokesTool?: boolean;
  dispatchesAgent?: boolean;
  mutatesSource?: boolean;
  appliesPatch?: boolean;
  promotesMemory?: boolean;
  activatesRetrieval?: boolean;
  releasesSoftware?: boolean;
  createsAuthority?: boolean;
  containsRawPrivateReasoning?: boolean;
}

export interface WorkflowEvidenceReferenceData {
  evidenceReferenceId?: string;
  evidenceId?: string;
  evidenceType?: string;
  evidenceLabel?: string;
  safeSummary?: string;
  isApproval?: boolean;
  isExecutionPermission?: boolean;
  isPolicySatisfaction?: boolean;
  isWorkflowTransition?: boolean;
  isMemoryPromotion?: boolean;
  isSourceApply?: boolean;
}

export interface WorkflowGroundingReferenceData {
  groundingReferenceId?: string;
  groundingId?: string;
  groundingType?: string;
  claim?: string;
  safeSummary?: string;
  groundingIsAuthority?: boolean;
}

export interface WorkflowStepSummaryData {
  workflowRunStepId: string;
  workflowRunId: string;
  projectId: string;
  stepKey: string;
  stepName: string;
  stepType: string;
  status: string;
  sequenceNumber?: number;
  agentRole?: string;
  agentId?: string;
  subjectType?: string;
  subjectId?: string;
  correlationId?: string;
  causationId?: string;
  evidenceReferenceCount?: number;
  groundingReferenceCount?: number;
  authorityFlags?: WorkflowAuthorityFlagsData;
  createdUtc?: string;
}

export interface WorkflowStepDetailData extends WorkflowStepSummaryData {
  safeSummary?: string;
  evidenceReferences?: WorkflowEvidenceReferenceData[];
  groundingReferences?: WorkflowGroundingReferenceData[];
}

export interface WorkflowRunSummaryData {
  workflowRunId: string;
  projectId: string;
  workflowType: string;
  workflowName: string;
  status: string;
  subjectType?: string;
  subjectId?: string;
  correlationId?: string;
  causationId?: string;
  stepCount?: number;
  evidenceReferenceCount?: number;
  groundingReferenceCount?: number;
  authorityFlags?: WorkflowAuthorityFlagsData;
  createdUtc?: string;
}

export interface WorkflowRunDetailData extends WorkflowRunSummaryData {
  subjectSummary?: string;
  steps?: WorkflowStepSummaryData[];
  evidenceReferences?: WorkflowEvidenceReferenceData[];
  groundingReferences?: WorkflowGroundingReferenceData[];
}

export interface WorkflowRunListData {
  runs?: WorkflowRunSummaryData[];
  issues?: WorkflowReadOnlyIssue[];
}

export interface WorkflowStepListData {
  steps?: WorkflowStepSummaryData[];
  issues?: WorkflowReadOnlyIssue[];
}

export interface WorkflowReadOnlyApiBoundary {
  readOnlyInspection?: boolean;
  workflowStatusIsAction?: boolean;
  evidenceIsPermission?: boolean;
  groundingIsAuthority?: boolean;
  endpointAccessIsExecutionPermission?: boolean;
  apiResponseStatusIsGovernance?: boolean;
  modelOutputIsAuthority?: boolean;
  sourceApplied?: boolean;
  memoryPromoted?: boolean;
  releaseApproved?: boolean;
  approvalSatisfied?: boolean;
  humanReviewRequiredForSourceApply?: boolean;
  humanReviewRequiredForMemoryPromotion?: boolean;
}

export interface WorkflowReadOnlyApiEnvelope<TData> {
  status?: string;
  data?: TData | null;
  workflowRunId?: string;
  evidenceId?: string;
  boundary?: WorkflowReadOnlyApiBoundary;
  mutationOccurred?: boolean;
  humanApprovalRequired?: boolean;
  warnings?: string[];
  errors?: WorkflowReadOnlyIssue[];
}

export interface FrontendReadBoundary {
  readOnly?: boolean | null;
  statusOnly?: boolean | null;
  canCreateApproval?: boolean | null;
  canAcceptApproval?: boolean | null;
  canSatisfyPolicy?: boolean | null;
  canExecute?: boolean | null;
  canMutateSource?: boolean | null;
  canRollback?: boolean | null;
  canCommit?: boolean | null;
  canPush?: boolean | null;
  canCreatePullRequest?: boolean | null;
  canMarkReadyForReview?: boolean | null;
  canMerge?: boolean | null;
  canRelease?: boolean | null;
  canDeploy?: boolean | null;
  canPromoteMemory?: boolean | null;
  canContinueWorkflow?: boolean | null;
}

export interface FrontendOperationStatusReadModel {
  operationId: string;
  operationKind: string;
  subject: string;
  state: string;
  blockedReasons: string[];
  missingEvidence: string[];
  nextSafeActions: string[];
  forbiddenActions: string[];
  evidenceRefs: string[];
  receiptRefs: string[];
  authorityWarnings: string[];
  boundary: FrontendReadBoundary;
  observedAtUtc: string;
  expiresAtUtc?: string | null;
}

export interface FrontendPatchPackageMetadataReadModel {
  packageId: string;
  repository: string;
  branch: string;
  runId: string;
  patchHash: string;
  proposedFilePaths: string[];
  artifactRefs: string[];
  evidenceRefs: string[];
  receiptRefs: string[];
  reviewSummaryRef: string;
  knownRisksRef: string;
  boundary: FrontendReadBoundary;
}

export interface FrontendPatchPackageArtifactsReadModel {
  packageId: string;
  repository: string;
  branch: string;
  runId: string;
  patchHash: string;
  patchDiffText: string;
  reviewSummaryText: string;
  knownRisksText: string;
  validationSummaryText: string;
  validationOutcome: string;
  whatRan: string[];
  whatPassed: string[];
  whatFailed: string[];
  whatWasSkipped: string[];
  validationIsStale: boolean;
  proposedFilePaths: string[];
  artifactRefs: string[];
  evidenceRefs: string[];
  receiptRefs: string[];
  authorityWarnings: string[];
  boundary: FrontendReadBoundary;
}

export interface FrontendReadinessApiError {
  category?: string | null;
  code?: string | null;
  field?: string | null;
  message?: string | null;
}

export interface FrontendReadinessApiEnvelope<TData> {
  status: string;
  data?: TData | null;
  boundary: FrontendReadBoundary;
  mutationOccurred: boolean;
  warnings: string[];
  errors: FrontendReadinessApiError[];
}

export type ControlledActionRequestKind = 'SourceApply' | 'Commit' | 'Push' | 'DraftPullRequest' | 'Rollback';

export interface FrontendActionRequestBoundary {
  canCreateRequest?: boolean | null;
  canApprove?: boolean | null;
  canAcceptApproval?: boolean | null;
  canSatisfyPolicy?: boolean | null;
  canExecute?: boolean | null;
  canMutateSource?: boolean | null;
  canRollback?: boolean | null;
  canCommit?: boolean | null;
  canPush?: boolean | null;
  canCreatePullRequest?: boolean | null;
  canMarkReadyForReview?: boolean | null;
  canMerge?: boolean | null;
  canRelease?: boolean | null;
  canDeploy?: boolean | null;
  canPromoteMemory?: boolean | null;
  canContinueWorkflow?: boolean | null;
}

export type ControlledActionRequestCreateRequest = components['schemas']['ControlledActionRequestCreateRequest'];

export interface ControlledActionRequestCreateResponse {
  requestId: string;
  operationId: string;
  requestKind: string;
  state: string;
  blockedReasons: string[];
  missingEvidence: string[];
  nextSafeActions: string[];
  forbiddenActions: string[];
  evidenceRefs: string[];
  receiptRefs: string[];
  authorityWarnings: string[];
  boundary: FrontendActionRequestBoundary;
  requestCreated: boolean;
  executionStarted: boolean;
  sourceMutated: boolean;
  workflowContinued: boolean;
}

// ── P0-7: skeleton run — the walking-skeleton loop the work-item spine consumes ──

export interface SkeletonCriticPackageChange {
  filePath: string;
  description: string;
  isNewFile: boolean;
  isDeletion: boolean;
  diff: string;
  fullContentAfter?: string | null;
}

export interface SkeletonAuthoredTest {
  relativePath: string;
  content: string;
  coversCriterion: string;
}

export interface SkeletonCriticPackageCommandResult {
  displayName: string;
  exitCode: number;
  timedOut: boolean;
  durationMs: number;
  standardOutputRef?: string | null;
  standardErrorRef?: string | null;
}

export interface SkeletonCriticPackage {
  packageId: string;
  runId: string;
  proposalId: string;
  ticketId: number;
  projectId: number;
  ticketTitle: string;
  acceptanceCriteria: string;
  proposalSummary: string;
  proposalRationale: string;
  changes: SkeletonCriticPackageChange[];
  authoredTests: SkeletonAuthoredTest[];
  criterionCoverage: SkeletonCriterionCoverage[];
  commandResults: SkeletonCriticPackageCommandResult[];
  evidenceRefs: string[];
  workspaceRunSucceeded: boolean;
  boundary: string;
}

export interface SkeletonRunTimelineEntry {
  timestampUtc: string;
  eventType: string;
  message: string;
}

export interface SkeletonRunProposalTrace {
  proposalId: string;
  fileChangeCount: number;
  evidenceRef: string;
  evidenceExistsOnDisk: boolean;
  modelProvider: string;
  modelName: string;
}

export interface SkeletonRunTestAuthoringTrace {
  authored: boolean;
  authoredTestCount: number;
  skippedReason: string;
  modelProvider: string;
  modelName: string;
}

export interface SkeletonRunCriticPackageTrace {
  packageId: string;
  packagePath: string;
  existsOnDisk: boolean;
  announcedSha256: string;
  sha256OnDisk: string;
  hashVerified: boolean;
  criterionCount: number;
  uncoveredCriterionCount: number;
}

export interface SkeletonRunApprovalTrace {
  targetKind: string;
  targetId: string;
  targetHash: string;
  capabilityCode: string;
  haltObserved: boolean;
  continuationUnblocked: boolean;
  acceptedApprovalId: string;
}

export interface SkeletonRunApplyStageTrace {
  stage: string;
  succeeded: boolean;
  errors: string;
}

export interface SkeletonRunReceiptRef {
  name: string;
  path: string;
  existsOnDisk: boolean;
}

export interface SkeletonRunApplyTrace {
  applied: boolean;
  workspacePath: string;
  refusedReason: string;
  stages: SkeletonRunApplyStageTrace[];
  receipts: SkeletonRunReceiptRef[];
  attempts: SkeletonRunApplyAttemptTrace[];
}

export interface SkeletonRunApplyAttemptTrace {
  attemptId: string;
  attemptNumber: number;
  requestedAction: string;
  requestedByUserId: string;
  reason: string;
  status: string;
  startedUtc: string;
  completedUtc?: string | null;
  workspacePath: string;
  interruptedStage: string;
  refusedReason: string;
  mutationState: string;
  stages: SkeletonRunApplyStageTrace[];
  availableActions: string[];
}

// REPAIR-1: one bounded repair attempt, reconstructed from durable events.
// A repair attempt is proposal-shaped work, never authority — its presence is
// honesty about the mess, not a mark against the run.
export interface SkeletonRunRepairAttemptTrace {
  attemptNumber: number;
  failureKind: string;
  failedCommand: string;
  repairProposalId: string;
  modelProvider: string;
  modelName: string;
  repairProposalEvidenceExistsOnDisk: boolean;
}

export interface SkeletonRunAgentConfigurationSnapshot {
    snapshotId: string;
    workItemId: number;
    runId: string;
    role: string;
    profileVersion?: number | null;
    profileScopeLayer: string;
    connectionId: string;
    provider: string;
    controlledEndpointIdentity: string;
    model: string;
    timeoutSeconds: number;
    inputTokenLimit?: number | null;
    outputTokenLimit?: number | null;
    temperature?: number | null;
    skillVersion: string;
    skillHash: string;
    personalityVersion: string;
    personalityHash: string;
    effectiveProfileHash: string;
    createdUtc: string;
    boundary: string;
  }

  export interface SkeletonRunReport {
  runId: string;
  projectId: number;
  ticketId: number;
  status: string;
    summary: string;
    timeline: SkeletonRunTimelineEntry[];
    agentConfigurations?: SkeletonRunAgentConfigurationSnapshot[];
  /**
   * The FINAL/CURRENT proposal — the one the gate, critic package, and approval
   * hash bind to. After a successful bounded repair this is the repaired
   * proposal, never the failed original.
   */
  proposal?: SkeletonRunProposalTrace | null;
  /**
   * REPAIR-1: the original failed proposal, populated ONLY when bounded repair
   * replaced it. Preserved history — it exists, and it is not the gate proposal.
   */
  initialProposal?: SkeletonRunProposalTrace | null;
  testAuthoring?: SkeletonRunTestAuthoringTrace | null;
  criticPackage?: SkeletonRunCriticPackageTrace | null;
  approval?: SkeletonRunApprovalTrace | null;
  criticReviews: SkeletonRunCriticReviewTrace[];
  findingDispositions: SkeletonRunFindingDispositionTrace[];
  repairAttempts: SkeletonRunRepairAttemptTrace[];
  apply?: SkeletonRunApplyTrace | null;
  gaps: string[];
  loopComplete: boolean;
  boundary: string;
}

// The human gate's own governed surface: recording an accepted approval. The
// backend owns identity, timestamps, and every derived authority field — the
// client may only describe WHAT is being approved.
export type CreateAcceptedApprovalUiRequest = components['schemas']['CreateAcceptedApprovalRequest'] & {
  approvalTargetKind: string;
  approvalTargetId: string;
  approvalTargetHash: string;
  capabilityCode: string;
  approvalPurpose: string;
  correlationId: string;
  causationId: string;
  evidenceReferences: string[];
  boundaryMaxims: string[];
  clientRequestId?: string | null;
};

export interface AcceptedApprovalApiError {
  category: string;
  code: string;
  field: string;
  message: string;
}

export interface AcceptedApprovalEnvelope<TData> {
  status: string;
  data?: TData | null;
  acceptedApprovalId?: string | null;
  warnings: string[];
  errors: AcceptedApprovalApiError[];
}

export interface AcceptedApprovalReadModelUi {
  acceptedApprovalId: string;
  approvalTargetKind: string;
  approvalTargetId: string;
  approvalTargetHash?: string | null;
  capabilityCode?: string | null;
  approvalPurpose?: string | null;
  approvedByActorDisplayName?: string | null;
  acceptedAtUtc?: string | null;
  expiresAtUtc?: string | null;
}

// ── P1-4/P1-7: coverage matrix + critic review + dispositions ──

export interface SkeletonCriterionCoverage {
  criterion: string;
  covered: boolean;
  coveringTests: string[];
}

export interface SkeletonCriticReviewFinding {
  findingId: string;
  severity: string;
  title: string;
  problem: string;
  whyItMatters: string;
  requiredFix: string;
  blocksMerge: boolean;
}

export interface SkeletonGroundTruthCheck {
  checkName: string;
  passed: boolean;
  expected: string;
  actual: string;
  detail: string;
  blocksMerge: boolean;
}

export interface SkeletonGroundTruthVerification {
  checks: SkeletonGroundTruthCheck[];
  mismatches: SkeletonGroundTruthCheck[];
  boundary: string;
}

export interface SkeletonCriticReviewOutcome {
  succeeded: boolean;
  failureReason: string;
  criticAgentRunId: string;
  reviewId: string;
  verdict: string;
  findings: SkeletonCriticReviewFinding[];
  groundTruth?: SkeletonGroundTruthVerification | null;
  boundary: string;
}

export interface SkeletonFindingDispositionOutcome {
  succeeded: boolean;
  failureReason: string;
  findingId: string;
  disposition: string;
  boundary: string;
}

export interface SkeletonRunCriticReviewTrace {
  criticAgentRunId: string;
  reviewId: string;
  verdict: string;
  findingCount: number;
  blockingFindingCount: number;
  findingIds: string[];
  packageSha256: string;
  groundTruthCheckCount: number;
  groundTruthMismatchCount: number;
  modelProvider: string;
  modelName: string;
}

export interface SkeletonRunFindingDispositionTrace {
  findingId: string;
  disposition: string;
  reason: string;
  decidedByUserId: string;
}

// ── P2-1..P2-7: batch (dependency map → plan → run) + gate recommendation ──

export interface SkeletonBatchDependencyEdge {
  fromTicketId: number;
  toTicketId: number;
  kind: string;
  reason: string;
  sharedPaths: string[];
}

export interface SkeletonBatchMap {
  projectId: number;
  ticketIds: number[];
  edges: SkeletonBatchDependencyEdge[];
  warnings: string[];
  boundary: string;
}

export interface SkeletonBatchMapOutcome {
  succeeded: boolean;
  failureReason: string;
  mapId: string;
  detectedAtUtc: string;
  map?: SkeletonBatchMap | null;
}

export interface SkeletonBatchWave {
  waveNumber: number;
  ticketIds: number[];
}

export interface SkeletonBatchCycleBlocker {
  ticketIds: number[];
  detail: string;
}

export interface SkeletonBatchPlan {
  projectId: number;
  mapId: string;
  waves: SkeletonBatchWave[];
  cycleBlockers: SkeletonBatchCycleBlocker[];
  warnings: string[];
  schedulable: boolean;
  boundary: string;
}

export interface SkeletonBatchPlanOutcome {
  succeeded: boolean;
  failureReason: string;
  planId: string;
  plannedAtUtc: string;
  plan?: SkeletonBatchPlan | null;
}

export interface SkeletonBatchTicketStatus {
  ticketId: number;
  wave: number;
  runId: string;
  runStatus: string;
  eligible: boolean;
  waitingOn: string[];
}

export interface SkeletonBatchRunStatus {
  batchId: string;
  planId: string;
  projectId: number;
  requestedByUserId: string;
  startedAtUtc: string;
  tickets: SkeletonBatchTicketStatus[];
  batchComplete: boolean;
  boundary: string;
}

export interface SkeletonBatchRunOutcome {
  succeeded: boolean;
  failureReason: string;
  startedRuns: Record<string, string>;
  status?: SkeletonBatchRunStatus | null;
}

export interface SkeletonGateMeasurementInput {
  measurementId: string;
  catchRate: number;
  controlClean: boolean;
  reExecutionAvailable: boolean;
  verified: boolean;
  measuredAtUtc: string;
}

export interface SkeletonGateRecommendation {
  runId: string;
  tier: string;
  recommendation: string;
  reasons: string[];
  measurementInput?: SkeletonGateMeasurementInput | null;
  boundary: string;
}

// ── AG-1: agent profiles (voice + model, never authority) ──

export interface SkeletonAgentProfile {
  role: string;
  displayName: string;
  builtInDefaultName: string;
  builtInDefaultVersion: string;
  aiConnectionId: string;
  provider: string;
  model: string;
  baseUrl: string;
  timeoutSeconds: number;
  skill: string;
  personality: string;
  boundary: string;
}

export interface SkeletonAgentProfileFieldSource {
  field: string;
  sourceLayer: string;
  sourceLabel: string;
  version?: string | null;
  inherited: boolean;
  detail: string;
}

export interface EffectiveSkeletonAgentProfile {
  role: string;
  displayName: string;
  aiConnectionId: string;
  provider: string;
  model: string;
  timeoutSeconds: number;
  effectiveSkill: string;
  effectivePersonality: string;
  fieldSources: SkeletonAgentProfileFieldSource[];
  builtInDefaultVersion: string;
  tenantProfileVersion?: string | null;
  projectProfileVersion?: string | null;
  publishedVersion?: number | null;
  publishedScopeLayer: string;
  effectiveHash: string;
  boundary: string;
}

export interface SkeletonAgentProfileUpdate {
  aiConnectionId: string;
  provider: string;
  model: string;
  timeoutSeconds: number;
  skill: string;
  personality: string;
}

export interface SkeletonAgentProfileOutcome {
  succeeded: boolean;
  failureReason: string;
  profile?: SkeletonAgentProfile | null;
}

export interface SkeletonAgentProfileValidationIssue {
  code: string;
  field: string;
  message: string;
}

export interface SkeletonAgentProfileDraft {
  role: string;
  revision: number;
  basePublishedVersion: number;
  values: SkeletonAgentProfileUpdate;
  isValid: boolean;
  validationIssues: SkeletonAgentProfileValidationIssue[];
  updatedAtUtc: string;
}

export interface SkeletonAgentProfileDraftWriteRequest extends SkeletonAgentProfileUpdate {
  expectedRevision: number;
}

export interface SkeletonAgentProfilePublishRequest {
  expectedRevision: number;
  reason: string;
}

export interface SkeletonAgentProfilePublishedVersion {
  version: number;
  role: string;
  values: SkeletonAgentProfileUpdate;
  reason: string;
  actorUserId: number;
  publishedAtUtc: string;
  scopeLayer: string;
  tenantId?: number | null;
  projectId?: number | null;
}

export interface SkeletonAgentProfileRunUsage {
  runId: string;
  projectId: number;
  workItemId: number;
  capturedAtUtc: string;
}

export interface SkeletonAgentProfileHistoryView {
  version: SkeletonAgentProfilePublishedVersion;
  runUsage: SkeletonAgentProfileRunUsage[];
  usageBoundary: string;
}

export interface SkeletonAgentProfileDraftOutcome {
  succeeded: boolean;
  code: string;
  failureReason: string;
  currentRevision: number;
  draft?: SkeletonAgentProfileDraft | null;
  publishedVersion?: SkeletonAgentProfilePublishedVersion | null;
  profile?: SkeletonAgentProfile | null;
}

export interface SkeletonAgentProfileDraftTestOutcome {
  succeeded: boolean;
  status: string;
  failureReason: string;
  validationIssues: SkeletonAgentProfileValidationIssue[];
  executedAtUtc: string;
  summary: string;
  boundary: string;
}

export interface SkeletonAgentProfileResetRequest {
  expectedRevision: number;
  scope: 'Field' | 'Agent' | 'BuiltIn' | 'Project' | 'Tenant';
  field: string;
  reason: string;
}

export interface SkeletonAgentProfileRestoreRequest {
  expectedRevision: number;
  reason: string;
}

export interface AiConnectionMetadata {
  id: string;
  tenantId: number;
  displayName: string;
  providerKind: string;
  controlledEndpointId: string;
  controlledEndpoint: string;
  credentialConfigured: boolean;
  credentialStatus: string;
  supportedPurposes: string[];
  purposeDescription: string;
  lastSuccessfulTestUtc?: string | null;
  lastFailedTestUtc?: string | null;
  availableModels: string[];
  enabled: boolean;
  tenantAvailable: boolean;
  projectAvailable: boolean;
  credentialRotatedUtc?: string | null;
  credentialRevokedUtc?: string | null;
  createdByUserId: number;
  createdUtc?: string | null;
  updatedByUserId: number;
  updatedUtc?: string | null;
  version: string;
  boundary: string;
}

// ── PROJECT-0..3: provisioning readiness (computed server-side, never asserted) ──

export interface AiConnectionCredentialWriteRequest {
  credential: string;
  reason?: string | null;
}

export interface AiConnectionCredentialRevokeRequest {
  reason?: string | null;
}

export interface AiConnectionCredentialMutationOutcome {
  succeeded: boolean;
  failureReason?: string | null;
  connection?: AiConnectionMetadata | null;
  boundary: string;
}

export interface AgentConfigurationPackEntry {
  role: string | number;
  values: SkeletonAgentProfileUpdate;
  logicalConnectionName: string;
  builtInDefaultVersion: string;
  sourcePublishedVersion: number;
}

export interface AgentConfigurationPack {
  format: string;
  formatVersion: number;
  packId: string;
  exportedAtUtc: string;
  sourceScope: string;
  sourceTenantId: number;
  sourceProjectId?: number | null;
  profiles: AgentConfigurationPackEntry[];
  boundary: string;
}

export interface AgentConfigurationPackDifference {
  role: string | number;
  field: string;
  currentValue: string;
  importedValue: string;
  changed: boolean;
}

export interface AgentConfigurationPackPreview {
  succeeded: boolean;
  code: string;
  failureReason: string;
  targetScope: string;
  targetProjectId?: number | null;
  differences: AgentConfigurationPackDifference[];
  expectedRevisions: Record<string, number>;
  draftOnly: boolean;
  sourceProvenance: string;
  boundary: string;
}

export interface AgentConfigurationPackImportOutcome {
  succeeded: boolean;
  code: string;
  failureReason: string;
  createdDrafts: SkeletonAgentProfileDraft[];
  published: boolean;
  preview: AgentConfigurationPackPreview;
  boundary: string;
}

export interface AiConnectionTestOutcome {
  succeeded: boolean;
  status: string;
  failureReason?: string | null;
  httpStatusCode?: number | null;
  testedAtUtc: string;
  connection?: AiConnectionMetadata | null;
  boundary: string;
}

export interface ProvisioningCheckUi {
  code: string;
  name: string;
  label: string;
  /** Confirmed | Detected | NeedsConfirmation | Missing | Unsafe | NotEvaluated */
  state: string;
  summary: string;
  evidence: string;
  remedy: string;
  blocking: boolean;
  /** Detected candidate for wizard prefill — a proposal, never a confirmation. */
  detectedValue: string;
  actionKind: string;
}

export interface ProvisioningNextActionUi {
  kind: string;
  checkCode?: string | null;
  allowed: boolean;
  reasonCode?: string | null;
  label: string;
  nextSafeAction: string;
}

export interface ProjectProvisioningReadinessUi {
  projectId: number;
  isReady: boolean;
  blockedCount: number;
  blockedStates: string[];
  checks: ProvisioningCheckUi[];
  nextAction: ProvisioningNextActionUi;
  /** The detected architecture profile awaiting one-click human confirmation, when present. */
  proposedProfile?: Record<string, unknown> | null;
  boundary: string;
}

export interface CodeIndexResultUi {
  filesScanned: number;
  filesAdded: number;
  filesUpdated: number;
  filesUnchanged: number;
  filesSkipped: number;
  storedFileCount: number;
  directoryNotFound: boolean;
  errorMessage?: string | null;
  isEmpty: boolean;
}

export interface ProjectProvisioningActionResultUi {
  allowed: boolean;
  status: string;
  reasonCode?: string | null;
  message: string;
  capability: string;
  changed: boolean;
  correlationId: string;
  indexResult?: CodeIndexResultUi | null;
  profile?: Record<string, unknown> | null;
  readiness?: ProjectProvisioningReadinessUi | null;
}

// ── AFFORDANCE-1: planned surfaces refuse honestly ──

/**
 * The refusal envelope returned by planned-but-unbuilt surfaces (HTTP 501).
 * Shaped like a governed refusal on purpose — allowed is always false and reason is always
 * 'NotImplemented' — so the UI renders it through the same refusal discipline as any other
 * blocked action. Mirrors IronDev.Api PlannedSurfaceEnvelope.
 */
export interface PlannedSurfaceEnvelope {
  allowed: boolean;
  reason: string;
  surface: string;
  detail: string;
  plannedSlice: string;
  nextSafeAction: string;
  boundary: string;
  correlationId: string;
}
