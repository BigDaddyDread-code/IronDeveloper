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

export type ProjectTicket = components['schemas']['ProjectTicket'];
export type ProjectSummary = components['schemas']['Project'];
export type BuildReadinessResult = components['schemas']['BuildReadinessResult'];
export type CreateProjectTicketRequest = components['schemas']['CreateProjectTicketRequest'];
export type ProjectImplementationPlan = components['schemas']['ProjectImplementationPlan'];
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
