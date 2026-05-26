import { useCallback, useEffect, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ApiStatus,
  BuildReadinessResult,
  CreateProjectTicketRequest,
  LinkedPromotionPackageSummary,
  LinkedRunSummary,
  LinkedRunStatus,
  TicketEvidenceLoadStatus,
  TicketEvidenceSummary,
  TicketRunReview,
  ProductAccessStatus,
  ProjectImplementationPlan,
  ProjectSummary,
  ProjectTicket,
  TicketCreateStatus,
  TicketDetailLoadStatus,
  RunReportSummary,
  TicketPlanStatus,
  TicketReadinessLoadStatus,
  TicketSaveStatus,
  TenantSummary,
  UserProfile
} from '../../api/types';
import type { CreateTicketDraft } from '../../components/CreateTicketPanel';
import type { TicketEditDraft } from '../../components/TicketEditForm';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { useWorkspaceNavigation } from '../../state/useWorkspaceNavigation';

const initialCreateDraft: CreateTicketDraft = {
  title: '',
  summary: '',
  type: 'Feature / Workflow',
  priority: 'Medium',
  acceptanceCriteria: ''
};

const initialEditDraft: TicketEditDraft = {
  title: '',
  summary: '',
  problem: '',
  proposedChange: '',
  type: '',
  priority: 'Medium',
  acceptanceCriteria: '',
  technicalNotes: '',
  unitTests: '',
  integrationTests: '',
  manualTests: '',
  regressionTests: '',
  buildValidation: ''
};

interface TicketsWorkspaceState {
  apiStatus: ApiStatus;
  accessStatus: ProductAccessStatus;
  apiBaseUrl: string;
  projectId: number | null;
  projectStatus: 'selected' | 'missing' | 'fallback';
  tokenConfigured: boolean;
  projectBadgeStatus: 'selected' | 'missing' | 'fallback';
  projectAccessBlocked: boolean;
  authLabel: string;
  tenants: TenantSummary[];
  projects: ProjectSummary[];
  selectedTenantId: number | null;
  selectedProjectId: number | null;
  tickets: ProjectTicket[];
  selectedTicket: ProjectTicket | null;
  ticketDetailStatus: TicketDetailLoadStatus;
  ticketDetailMessage: string;
  readiness: BuildReadinessResult | null;
  readinessStatus: TicketReadinessLoadStatus;
  readinessMessage: string;
  implementationPlan: ProjectImplementationPlan | null;
  planStatus: TicketPlanStatus;
  planMessage: string;
  evidenceSummary: TicketEvidenceSummary | null;
  evidenceStatus: TicketEvidenceLoadStatus;
  evidenceMessage: string;
  runReview: TicketRunReview | null;
  runReviewStatus: TicketEvidenceLoadStatus;
  runReviewMessage: string;
  isRunReviewOpen: boolean;
  reviewLatestRunBlockedReason: string | null;
  startDisposableRunBlockedReason: string | null;
  isEditingTicket: boolean;
  editDraft: TicketEditDraft;
  saveStatus: TicketSaveStatus;
  saveMessage: string;
  isEditDirty: boolean;
  editValidationMessage: string | null;
  editBlockedReason: string | null;
  isCreatePanelOpen: boolean;
  createDraft: CreateTicketDraft;
  createStatus: TicketCreateStatus;
  createMessage: string;
  createBlockedReason: string | null;
  createdTicketId: number | null;
  selectedTicketId: number | null;
  ticketMessage: string;
  tokenDraft: string;
  email: string;
  password: string;
  isTokenConfigOpen: boolean;
  isBusy: boolean;
  errorMessage: string | null;
}

interface TicketsWorkspaceActions {
  onSelectTicket: (ticketId: number) => void;
  onEditTicket: () => void;
  onEditDraftChange: (draft: TicketEditDraft) => void;
  onSaveTicket: () => void;
  onCancelEditTicket: () => void;
  onRefreshPlan: () => void;
  onRefreshReadiness: () => void;
  onRefreshEvidence: () => void;
  onStartDisposableRun: () => void;
  onReviewLatestRun: () => void;
  onRefreshRunReview: () => void;
  onDismissRunReview: () => void;
  onOpenPromotionReview: () => void;
  onCreateDraftChange: (draft: CreateTicketDraft) => void;
  onSubmitCreateTicket: () => void;
  onCancelCreateTicket: () => void;
  onConfigureToken: () => void;
  onRetry: () => void;
  onTokenDraftChange: (value: string) => void;
  onEmailChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onSaveToken: () => void;
  onSignIn: () => void;
  onSelectTenant: (tenantId: number) => void;
  onSelectProject: (projectId: number) => void;
  onOpenCreate: () => void;
  onRefreshTickets: () => void;
}

export interface TicketsWorkspaceViewModel extends TicketsWorkspaceState {
  createBlockedReason: string | null;
  createTicket: () => void;
  refresh: () => void;
  actions: TicketsWorkspaceActions;
}

export function useTicketsWorkspace() {
  const session = useSessionContext();
  const project = useProjectContext();
  const navigation = useWorkspaceNavigation();
  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [selectedTicketId, setSelectedTicketId] = useState<number | null>(null);
  const [selectedTicketDetail, setSelectedTicketDetail] = useState<ProjectTicket | null>(null);
  const [ticketDetailStatus, setTicketDetailStatus] = useState<TicketDetailLoadStatus>('idle');
  const [ticketDetailMessage, setTicketDetailMessage] = useState('Select a ticket to load detail.');
  const [readiness, setReadiness] = useState<BuildReadinessResult | null>(null);
  const [readinessStatus, setReadinessStatus] = useState<TicketReadinessLoadStatus>('idle');
  const [readinessMessage, setReadinessMessage] = useState('Build readiness has not been checked for this ticket.');
  const [implementationPlan, setImplementationPlan] = useState<ProjectImplementationPlan | null>(null);
  const [planStatus, setPlanStatus] = useState<TicketPlanStatus>('idle');
  const [planMessage, setPlanMessage] = useState('Plan has not been refreshed for this ticket.');
  const [evidenceSummary, setEvidenceSummary] = useState<TicketEvidenceSummary | null>(null);
  const [evidenceStatus, setEvidenceStatus] = useState<TicketEvidenceLoadStatus>('idle');
  const [evidenceMessage, setEvidenceMessage] = useState('Execution evidence has not been loaded for this ticket.');
  const [runReview, setRunReview] = useState<TicketRunReview | null>(null);
  const [runReviewStatus, setRunReviewStatus] = useState<TicketEvidenceLoadStatus>('idle');
  const [runReviewMessage, setRunReviewMessage] = useState('No run review has been opened for this ticket.');
  const [isRunReviewOpen, setIsRunReviewOpen] = useState(false);
  const [isCreatePanelOpen, setIsCreatePanelOpen] = useState(false);
  const [createDraft, setCreateDraft] = useState<CreateTicketDraft>(initialCreateDraft);
  const [createStatus, setCreateStatus] = useState<TicketCreateStatus>('idle');
  const [createMessage, setCreateMessage] = useState('Create a new IronDev ticket in the selected project.');
  const [createdTicketId, setCreatedTicketId] = useState<number | null>(null);
  const [isEditingTicket, setIsEditingTicket] = useState(false);
  const [editDraft, setEditDraft] = useState<TicketEditDraft>(initialEditDraft);
  const [saveStatus, setSaveStatus] = useState<TicketSaveStatus>('idle');
  const [saveMessage, setSaveMessage] = useState('Ticket is clean.');
  const [ticketMessage, setTicketMessage] = useState('Waiting for API health check.');

  const accessStatus = project.accessStatus;
  const selectedProjectId = project.selectedProjectId;
  const selectedTenantId = project.selectedTenantId;
  const projectSelectionMode = project.projectSelectionMode;
  const projectStatus = projectSelectionMode === 'api' ? 'selected' : projectSelectionMode === 'fallback-config' ? 'fallback' : 'missing';
  const selectedTicketFromQueue = tickets.find((ticket) => ticket.id === selectedTicketId) ?? tickets[0] ?? null;
  const selectedTicket = selectedTicketDetail?.id === selectedTicketId ? selectedTicketDetail : selectedTicketFromQueue;
  const selectedTicketIdForList = selectedTicket?.id ?? null;
  const tokenConfigured = session.tokenConfigured;
  const productAccessBlocked = !['ready', 'emptyTickets', 'loadingTickets'].includes(accessStatus);
  const createBlockedReason = getCreateTicketBlocker(
    session.apiStatus,
    accessStatus,
    tokenConfigured,
    selectedTenantId,
    selectedProjectId,
    projectSelectionMode
  );
  const ticketActionBlockedReason = getTicketActionBlocker(
    session.apiStatus,
    accessStatus,
    tokenConfigured,
    selectedTenantId,
    selectedProjectId,
    projectSelectionMode,
    selectedTicketIdForList
  );
  const startDisposableRunBlockedReason = useMemo(() => {
    if (ticketActionBlockedReason) {
      return ticketActionBlockedReason;
    }

    if (readinessStatus === 'loaded' && readiness && !readiness.isReady) {
      return readiness.message ?? readiness.blockingIssues?.[0] ?? 'Resolve build readiness blockers before starting a disposable run.';
    }

    return null;
  }, [readiness, readinessStatus, ticketActionBlockedReason]);
  const isEditDirty = selectedTicket ? !areEditDraftsEqual(editDraft, draftFromTicket(selectedTicket)) : false;
  const editValidationMessage = useMemo(() => validateEditDraft(editDraft), [editDraft]);
  const isBusy = project.isRefreshing || session.isConnectionBusy || session.isAuthBusy;
  const reviewLatestRunBlockedReason = useMemo(() => {
    if (!selectedTicket) {
      return 'Select a ticket before reviewing execution runs.';
    }

    if (!evidenceSummary) {
      return 'Execution evidence has not been loaded for this ticket.';
    }

    if (evidenceSummary.latestRun) {
      return null;
    }

    return 'No linked execution run yet.';
  }, [evidenceSummary, selectedTicket]);

  const resetTicketWorkflowState = useCallback(() => {
    setImplementationPlan(null);
    setPlanStatus('idle');
    setPlanMessage('Plan has not been refreshed for this ticket.');
    setIsEditingTicket(false);
    setEditDraft(initialEditDraft);
    setSaveStatus('idle');
    setSaveMessage('Ticket is clean.');
    setTicketDetailMessage('Select a ticket to load detail.');
    setTicketDetailStatus('idle');
    setSelectedTicketDetail(null);
    setReadiness(null);
    setReadinessStatus('idle');
    setEvidenceSummary(null);
    setEvidenceStatus('idle');
    setEvidenceMessage('Execution evidence has not been loaded for this ticket.');
    setRunReview(null);
    setRunReviewStatus('idle');
    setRunReviewMessage('No run review has been opened for this ticket.');
    setIsRunReviewOpen(false);
    setReadinessMessage('Build readiness has not been checked for this ticket.');
  }, []);

  const setTicketAccessBlocked = useCallback(
    (status: Exclude<ProductAccessStatus, 'ready' | 'emptyTickets' | 'loadingTickets'>) => {
      project.setProjectAccessStatus(status);
      setTicketMessage(status === 'authInvalid' ? 'Sign in session expired.' : 'Ticket workflow is currently blocked.');
      setTickets([]);
      setSelectedTicketId(null);
      resetTicketWorkflowState();
    },
    [project, resetTicketWorkflowState]
  );

  const loadTickets = useCallback(async () => {
    if (!selectedProjectId) {
      setTickets([]);
      setSelectedTicketId(null);
      setTicketAccessBlocked('projectRequired');
      return;
    }

    try {
      const ticketResult = await session.client.getProjectTickets(selectedProjectId);

      setTickets(ticketResult.tickets);
      setTicketMessage(ticketResult.message);
      setSelectedTicketId((current) => {
        const nextSelected = current && ticketResult.tickets.some((ticket) => ticket.id === current);
        return nextSelected ? current : ticketResult.tickets[0]?.id ?? null;
      });
      setSelectedTicketDetail(null);
      setIsCreatePanelOpen(false);
      resetTicketWorkflowState();
      setReadiness(null);
      setReadinessStatus('idle');

      if (ticketResult.status === 'connected') {
        project.setProjectAccessStatus(ticketResult.tickets.length === 0 ? 'emptyTickets' : 'ready');
      } else if (ticketResult.status === 'authRequired') {
        setTicketAccessBlocked('authInvalid');
      } else if (ticketResult.status === 'disconnected') {
        setTicketAccessBlocked('apiOffline');
      } else if (ticketResult.status === 'error') {
        setTicketAccessBlocked('apiError');
      } else {
        setTicketAccessBlocked('apiError');
      }
    } catch (error) {
      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setTicketAccessBlocked('authInvalid');
      } else if (error instanceof IronDevApiError) {
        setTicketAccessBlocked('apiError');
      } else {
        setTicketAccessBlocked('apiOffline');
      }
    }
  }, [resetTicketWorkflowState, selectedProjectId, session.client, project, setTicketAccessBlocked]);

  useEffect(() => {
    if (accessStatus === 'loadingTickets') {
      void loadTickets();
      return;
    }

    if (productAccessBlocked) {
      setTickets([]);
      setSelectedTicketId(null);
      resetTicketWorkflowState();
    }
  }, [accessStatus, loadTickets, productAccessBlocked, resetTicketWorkflowState]);

  useEffect(() => {
    if (productAccessBlocked || selectedTicketId || tickets.length === 0) {
      return;
    }

    setSelectedTicketId(tickets[0]?.id ?? null);
  }, [productAccessBlocked, selectedTicketId, tickets]);

  useEffect(() => {
    if (productAccessBlocked || !selectedProjectId || !selectedTicketId) {
      setSelectedTicketDetail(null);
      setTicketDetailStatus('idle');
      setTicketDetailMessage('Select a ticket to load detail.');
      setEvidenceSummary(null);
      setEvidenceStatus('idle');
      setEvidenceMessage('Execution evidence has not been loaded for this ticket.');
      setRunReview(null);
      setRunReviewStatus('idle');
      setRunReviewMessage('No run review has been opened for this ticket.');
      setIsRunReviewOpen(false);
      return;
    }

    const controller = new AbortController();
    setTicketDetailStatus('loading');
    setTicketDetailMessage('Loading selected ticket detail through IronDev.Api...');
    setReadiness(null);
    setReadinessStatus('idle');
    setReadinessMessage('Build readiness has not been checked for this ticket.');
    setImplementationPlan(null);
    setPlanStatus('idle');
    setPlanMessage('Plan has not been refreshed for this ticket.');
    setEvidenceSummary(null);
    setEvidenceStatus('loading');
    setEvidenceMessage('Execution evidence is being resolved from available run reports.');
    setRunReview(null);
    setRunReviewStatus('idle');
    setRunReviewMessage('No run review has been opened for this ticket.');
    setIsRunReviewOpen(false);
    setIsEditingTicket(false);
    setSaveStatus('idle');
    setSaveMessage('Ticket is clean.');

    session.client
      .getProjectTicket(selectedProjectId, selectedTicketId, controller.signal)
      .then((ticket) => {
        if (controller.signal.aborted) {
          return;
        }

        setSelectedTicketDetail(ticket);
        setEditDraft(draftFromTicket(ticket));
        setTicketDetailStatus('loaded');
        setTicketDetailMessage('Ticket detail loaded.');
      })
      .catch((error) => {
        if (controller.signal.aborted) {
          return;
        }

        setSelectedTicketDetail(null);
        setTicketDetailStatus('error');
        setTicketDetailMessage(
          error instanceof IronDevApiError ? `Ticket detail failed with HTTP ${error.status}.` : 'Ticket detail request could not reach IronDev.Api.'
        );
        setEvidenceSummary({
          ticketId: selectedTicketId,
          status: 'error',
          message: 'Could not load ticket detail; evidence could not be refreshed.',
          latestRun: null,
          latestPromotionPackage: null,
          linkedTraceCount: 0,
          linkedDocumentCount: 0,
          linkedDecisionCount: 0,
          linkedRunCount: 0,
          hasBlockingWarnings: true,
          blockedActions: ['Ticket detail failed to load. Refresh evidence after ticket detail resolves.'],
          nextSafeAction: 'Review readiness'
        });
        setEvidenceStatus('error');
        setEvidenceMessage('Could not refresh execution evidence because ticket detail failed to load.');
      });

    return () => controller.abort();
  }, [productAccessBlocked, selectedProjectId, selectedTicketId, session.client]);

  const refresh = useCallback(async () => {
    resetTicketWorkflowState();
    await project.refreshProjectContext();
  }, [project, resetTicketWorkflowState]);

  const openCreatePanel = useCallback(() => {
    setIsCreatePanelOpen(true);
    setIsEditingTicket(false);
    setCreatedTicketId(null);

    if (createBlockedReason) {
      setCreateStatus('error');
      setCreateMessage(createBlockedReason);
    } else {
      setCreateStatus('idle');
      setCreateMessage('Create a new IronDev ticket in the selected project.');
    }
  }, [createBlockedReason]);

  const closeCreatePanel = useCallback(() => {
    setIsCreatePanelOpen(false);
    setCreateStatus('idle');
    setCreateMessage('Create a new IronDev ticket in the selected project.');
    setCreatedTicketId(null);
  }, []);

  const createTicket = useCallback(async () => {
    setCreateStatus('validating');
    setCreatedTicketId(null);

    if (createBlockedReason) {
      setCreateStatus('error');
      setCreateMessage(createBlockedReason);
      return;
    }

    const title = createDraft.title.trim();
    const summary = createDraft.summary.trim();

    if (!title) {
      setCreateStatus('error');
      setCreateMessage('Title is required before IronDev can create a ticket.');
      return;
    }

    if (!summary) {
      setCreateStatus('error');
      setCreateMessage('Summary is required so the ticket has enough context.');
      return;
    }

    if (!selectedProjectId) {
      setCreateStatus('error');
      setCreateMessage('Select a project before creating a ticket.');
      return;
    }

    const request: CreateProjectTicketRequest = {
      title,
      summary,
      type: createDraft.type.trim() || undefined,
      priority: createDraft.priority.trim() || undefined,
      acceptanceCriteria: splitAcceptanceCriteria(createDraft.acceptanceCriteria),
      provenance: {
        source: 'tauri-shell',
        createdBy: project.userProfile?.displayName ?? 'tauri-shell',
        notes: 'Created from the Tauri Tickets cockpit.'
      }
    };

    setCreateStatus('submitting');
    setCreateMessage('Creating ticket through IronDev.Api...');

    try {
      const created = await session.client.createProjectTicket(selectedProjectId, request);
      const result = await session.client.getProjectTickets(selectedProjectId);
      const createdId = created.id ?? null;

      setTickets(result.tickets);
      setSelectedTicketId(createdId ?? result.tickets[0]?.id ?? null);
      setSelectedTicketDetail(created);
      setTicketDetailStatus('loaded');
      setTicketDetailMessage('Created ticket detail loaded.');
      setReadiness(null);
      setReadinessStatus('idle');
      setReadinessMessage('Build readiness has not been checked for this ticket.');
      setTicketMessage(`Loaded ${result.tickets.length} ticket(s) after create.`);
      setImplementationPlan(null);
      setPlanStatus('idle');
      setPlanMessage('Plan has not been refreshed for this ticket.');
      setIsEditingTicket(false);
      setEditDraft(draftFromTicket(created));
      setSaveStatus('idle');
      setSaveMessage('Ticket is clean.');
      setCreatedTicketId(createdId);
      setCreateStatus('success');
      setCreateMessage(createdId ? `IronDev ticket #${createdId} was created and selected.` : 'IronDev ticket was created.');
      setCreateDraft(initialCreateDraft);
      project.setProjectAccessStatus(result.tickets.length === 0 ? 'emptyTickets' : 'ready');
    } catch (error) {
      setCreateStatus('error');
      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setCreateMessage('IronDev.Api rejected the current token. Sign in again before creating tickets.');
      } else if (error instanceof IronDevApiError && error.status === 400) {
        setCreateMessage('IronDev.Api rejected the ticket payload. Check the title, summary, and acceptance criteria.');
      } else if (error instanceof IronDevApiError) {
        setCreateMessage(`Ticket creation failed with HTTP ${error.status}.`);
      } else {
        setCreateMessage('Ticket creation could not reach IronDev.Api.');
      }
    }
  }, [createBlockedReason, createDraft, project, selectedProjectId, session.client]);

  const handleSelectTicket = useCallback(
    (ticketId: number) => {
      if (isEditingTicket && isEditDirty) {
        setSaveStatus('validation');
        setSaveMessage('Save or cancel the current ticket changes before switching selection.');
        return;
      }

      setSelectedTicketId(ticketId);
      setIsCreatePanelOpen(false);
      setIsEditingTicket(false);
      setSaveStatus('idle');
      setSaveMessage('Ticket is clean.');
    },
    [isEditDirty, isEditingTicket]
  );

  const beginEditTicket = useCallback(() => {
    if (!selectedTicket) {
      setSaveStatus('validation');
      setSaveMessage('Select a ticket before editing.');
      return;
    }

    if (ticketActionBlockedReason) {
      setSaveStatus('validation');
      setSaveMessage(ticketActionBlockedReason);
      return;
    }

    setIsCreatePanelOpen(false);
    setEditDraft(draftFromTicket(selectedTicket));
    setSaveStatus('editing');
    setSaveMessage('Editing selected ticket.');
    setIsEditingTicket(true);
  }, [selectedTicket, ticketActionBlockedReason]);

  const cancelEditTicket = useCallback(() => {
    setEditDraft(selectedTicket ? draftFromTicket(selectedTicket) : initialEditDraft);
    setSaveStatus('idle');
    setSaveMessage('Ticket changes discarded.');
    setIsEditingTicket(false);
  }, [selectedTicket]);

  const saveTicket = useCallback(async () => {
    if (!selectedTicket || !selectedTicket.id) {
      setSaveStatus('validation');
      setSaveMessage('Select a ticket before saving.');
      return;
    }

    if (ticketActionBlockedReason) {
      setSaveStatus('validation');
      setSaveMessage(ticketActionBlockedReason);
      return;
    }

    const validationMessage = validateEditDraft(editDraft);
    if (validationMessage) {
      setSaveStatus('validation');
      setSaveMessage(validationMessage);
      return;
    }

    if (!isEditDirty) {
      setSaveStatus('idle');
      setSaveMessage('No changes to save.');
      return;
    }

    if (!selectedProjectId) {
      setSaveStatus('validation');
      setSaveMessage('Select a project before saving.');
      return;
    }

    setSaveStatus('saving');
    setSaveMessage('Saving ticket through IronDev.Api...');

    try {
      const savedTicket = await session.client.saveProjectTicket(
        selectedProjectId,
        buildTicketFromEditDraft(selectedTicket, editDraft, selectedProjectId)
      );
      setTickets((current) =>
        current.map((ticket) => (ticket.id === savedTicket.id ? { ...ticket, ...savedTicket } : ticket))
      );
      setSelectedTicketId(savedTicket.id ?? selectedTicket.id ?? null);
      setSelectedTicketDetail(savedTicket);
      setEditDraft(draftFromTicket(savedTicket));
      setIsEditingTicket(false);
      setSaveStatus('saved');
      setSaveMessage('Ticket saved through IronDev.Api.');
      setTicketMessage('Ticket saved and local queue state refreshed.');
    } catch (error) {
      setSaveStatus('error');
      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setSaveMessage('IronDev.Api rejected the current token. Sign in again before saving tickets.');
      } else if (error instanceof IronDevApiError && error.status === 400) {
        setSaveMessage('IronDev.Api rejected the ticket update. Check required fields.');
      } else if (error instanceof IronDevApiError) {
        setSaveMessage(`Ticket save failed with HTTP ${error.status}.`);
      } else {
        setSaveMessage('Ticket save could not reach IronDev.Api.');
      }
    }
  }, [editDraft, isEditDirty, project, selectedProjectId, selectedTicket, session.client, ticketActionBlockedReason]);

  const refreshEvidence = useCallback(async () => {
    if (!selectedTicket) {
      setEvidenceSummary(null);
      setEvidenceStatus('unavailable');
      setEvidenceMessage('Select a ticket to load execution evidence.');
      return;
    }

    setEvidenceStatus('loading');
    setEvidenceMessage('Loading execution evidence...');

    try {
      if (!selectedProjectId || !selectedTicket.id) {
        throw new IronDevApiError('Ticket evidence requires a selected project and ticket.', 400);
      }

      const summary = await session.client.getTicketEvidenceSummary(selectedProjectId, selectedTicket.id);
      setEvidenceSummary(summary);
      setEvidenceStatus('loaded');
      setEvidenceMessage(summary.message);
    } catch (error) {
      if (error instanceof IronDevApiError && error.status !== 404) {
        setEvidenceSummary({
          ticketId: selectedTicket.id ?? 0,
          status: 'error',
          message: `Execution evidence failed with HTTP ${error.status}.`,
          latestRun: null,
          latestPromotionPackage: null,
          linkedTraceCount: getLinkedTraceCount(selectedTicket),
          linkedDocumentCount: getLinkedDocumentCount(selectedTicket),
          linkedDecisionCount: 0,
          linkedRunCount: 0,
          hasBlockingWarnings: true,
          blockedActions: ['Execution evidence could not be loaded at this time.'],
          nextSafeAction: readiness?.isReady ? 'Start disposable run' : 'Refresh build readiness'
        });
        setEvidenceStatus('error');
        setEvidenceMessage(`Execution evidence failed with HTTP ${error.status}.`);
        return;
      }

      if (!(error instanceof IronDevApiError)) {
        setEvidenceStatus('unavailable');
        setEvidenceMessage('Evidence summary endpoint is unavailable. Falling back to local evidence resolution.');
      }

      try {
        const runReports = await session.client.getRunReports();
        const sortedRunReports = [...runReports].sort((left, right) => {
          const leftTime = Date.parse(left.startedUtc ?? left.completedUtc ?? '') || 0;
          const rightTime = Date.parse(right.startedUtc ?? right.completedUtc ?? '') || 0;

          return rightTime - leftTime;
        });

        const latestRelatedRun = getLatestTicketRelatedRun(sortedRunReports, selectedTicket);

        let latestPromotionPackage: LinkedPromotionPackageSummary | null = null;
        let latestRunSummary: LinkedRunSummary | null = null;

        if (latestRelatedRun) {
          const runSummary = mapRunSummary(latestRelatedRun);
          latestRunSummary = runSummary;

          try {
            if (!latestRelatedRun.runId) {
              throw new Error('No run id to resolve promotion data');
            }

            const runDetail = await session.client.getRunReport(latestRelatedRun.runId);
            if (runDetail?.promotionReview) {
              latestPromotionPackage = mapPromotionSummary(runDetail.promotionReview, latestRelatedRun.runId, runDetail);
            }
          } catch {
            // A run detail can transiently be unavailable even when a run exists.
            latestPromotionPackage = null;
          }
        }

        const summary = buildTicketEvidenceSummary({
          ticket: selectedTicket,
          readiness,
          readinessStatus,
          latestRelatedRun: latestRunSummary
        });

        setEvidenceSummary({
          ...summary,
          latestPromotionPackage,
          latestRun: latestRunSummary
        });

        setEvidenceStatus('loaded');
        setEvidenceMessage(summary.message);
      } catch {
        setEvidenceSummary({
          ticketId: selectedTicket.id ?? 0,
          status: 'error',
          message: 'Execution evidence could not be loaded.',
          latestRun: null,
          latestPromotionPackage: null,
          linkedTraceCount: getLinkedTraceCount(selectedTicket),
          linkedDocumentCount: getLinkedDocumentCount(selectedTicket),
          linkedDecisionCount: 0,
          linkedRunCount: 0,
          hasBlockingWarnings: true,
          blockedActions: ['Execution evidence could not be loaded at this time.'],
          nextSafeAction: readiness?.isReady ? 'Start disposable run' : 'Refresh build readiness'
        });
        setEvidenceStatus('error');
        setEvidenceMessage('Execution evidence could not be loaded.');
      }
    }
  }, [readiness, readinessStatus, selectedProjectId, selectedTicket, session.client]);

  const refreshReadiness = useCallback(async () => {
    if (!selectedProjectId || !selectedTicketIdForList) {
      setReadiness(null);
      setReadinessStatus('unavailable');
      setReadinessMessage('Select a project ticket before checking build readiness.');
      return;
    }

    setReadinessStatus('loading');
    setReadinessMessage('Checking build readiness through IronDev.Api...');

    try {
      const result = await session.client.getTicketBuildReadiness(selectedProjectId, selectedTicketIdForList);
      setReadiness(result);
      setReadinessStatus('loaded');
      setReadinessMessage(result.message ?? 'Build readiness returned without a message.');
      void refreshEvidence();
    } catch (error) {
      setReadiness(null);

      if (error instanceof IronDevApiError && error.status === 404) {
        setReadinessStatus('unavailable');
        setReadinessMessage('Build readiness is not available for this ticket yet.');
      } else if (error instanceof IronDevApiError) {
        setReadinessStatus('error');
        setReadinessMessage(`Build readiness failed with HTTP ${error.status}.`);
      } else {
        setReadinessStatus('error');
        setReadinessMessage('Build readiness request could not reach IronDev.Api.');
        void refreshEvidence();
      }
    }
  }, [refreshEvidence, selectedProjectId, selectedTicketIdForList, session.client]);

  const refreshImplementationPlan = useCallback(async () => {
    if (!selectedTicketIdForList) {
      setPlanStatus('unavailable');
      setPlanMessage('Select a ticket before refreshing the implementation plan.');
      return;
    }

    if (ticketActionBlockedReason) {
      setPlanStatus('unavailable');
      setPlanMessage(ticketActionBlockedReason);
      return;
    }

    setPlanStatus('loading');
    setPlanMessage('Refreshing implementation plan through IronDev.Api...');

    try {
      const plan = await session.client.getTicketImplementationPlan(selectedTicketIdForList);
      setImplementationPlan(plan);
      setPlanStatus('loaded');
      setPlanMessage(plan.proposedSteps || plan.goal ? 'Implementation plan loaded.' : 'Implementation plan returned without detailed steps.');
    } catch (error) {
      setImplementationPlan(null);

      if (error instanceof IronDevApiError && error.status === 404) {
        setPlanStatus('unavailable');
        setPlanMessage('Plan not available yet. The API has not exposed a plan for this ticket.');
      } else if (error instanceof IronDevApiError) {
        setPlanStatus('error');
        setPlanMessage(`Plan refresh failed with HTTP ${error.status}.`);
      } else {
        setPlanStatus('error');
        setPlanMessage('Plan refresh could not reach IronDev.Api.');
      }
    }
  }, [session.client, selectedTicketIdForList, ticketActionBlockedReason]);

  const loadRunReview = useCallback(
    async (runId: string | null = evidenceSummary?.latestRun?.runId ?? null) => {
      if (!runId) {
        setRunReviewStatus('unavailable');
        setRunReviewMessage('No linked run is available to review yet.');
        return;
      }

      if (!selectedProjectId || !selectedTicketIdForList) {
        setRunReviewStatus('unavailable');
        setRunReviewMessage('Select a project ticket before reviewing a run.');
        return;
      }

      setIsRunReviewOpen(true);
      setRunReviewStatus('loading');
      setRunReviewMessage('Loading run review through IronDev.Api...');

      try {
        const review = await session.client.getTicketRunReview(selectedProjectId, selectedTicketIdForList, runId);
        setRunReview(review);
        setRunReviewStatus('loaded');
        setRunReviewMessage('Run review loaded from IronDev.Api.');
        navigation.setSelectedRunId(runId);
      } catch (error) {
        const message =
          error instanceof IronDevApiError
            ? `Run review failed with HTTP ${error.status}.`
            : 'Run review could not reach IronDev.Api.';
        setRunReview(null);
        setRunReviewStatus(error instanceof IronDevApiError && error.status === 404 ? 'unavailable' : 'error');
        setRunReviewMessage(message);
      }
    },
    [evidenceSummary?.latestRun?.runId, navigation, selectedProjectId, selectedTicketIdForList, session.client]
  );

  const onStartDisposableRun = useCallback(async () => {
    if (startDisposableRunBlockedReason) {
      setEvidenceMessage(startDisposableRunBlockedReason);
      return;
    }

    if (!selectedProjectId || !selectedTicketIdForList) {
      setEvidenceMessage('Select a project ticket before starting a disposable run.');
      return;
    }

    setEvidenceStatus('loading');
    setEvidenceMessage('Starting disposable run through IronDev.Api...');

    try {
      const result = await session.client.startTicketBuildRun(selectedProjectId, selectedTicketIdForList, {});
      if (result.runId) {
        navigation.setSelectedRunId(result.runId);
      }

      setEvidenceMessage(result.message ?? `Disposable run ${result.runId} started.`);
      await refreshEvidence();
      if (result.runId) {
        await loadRunReview(result.runId);
      }
    } catch (error) {
      const message =
        error instanceof IronDevApiError
          ? `Disposable run start failed with HTTP ${error.status}.`
          : 'Disposable run start could not reach IronDev.Api.';
      setEvidenceSummary((current) =>
        current
          ? {
              ...current,
              status: 'error',
              message,
              hasBlockingWarnings: true,
              blockedActions: [...current.blockedActions, message]
            }
          : current
      );
      setEvidenceStatus('error');
      setEvidenceMessage(message);
    }
  }, [loadRunReview, navigation, refreshEvidence, selectedProjectId, selectedTicketIdForList, session.client, startDisposableRunBlockedReason]);

  const onReviewLatestRun = useCallback(() => {
    const latestRunId = evidenceSummary?.latestRun?.runId ?? null;

    if (!latestRunId || !evidenceSummary?.latestRun) {
      setEvidenceMessage('No linked run is available to review yet.');
      return;
    }

    navigation.setSelectedRunId(latestRunId);
    void loadRunReview(latestRunId);
  }, [evidenceSummary?.latestRun?.runId, evidenceSummary?.latestRun, loadRunReview, navigation]);

  const onRefreshRunReview = useCallback(() => {
    void loadRunReview(runReview?.runId ?? evidenceSummary?.latestRun?.runId ?? null);
  }, [evidenceSummary?.latestRun?.runId, loadRunReview, runReview?.runId]);

  const onDismissRunReview = useCallback(() => {
    setIsRunReviewOpen(false);
  }, []);

  const onOpenPromotionReview = useCallback(() => {
    const latestRunId = evidenceSummary?.latestPromotionPackage?.sourceRunId ?? evidenceSummary?.latestRun?.runId ?? null;
    if (!latestRunId || !evidenceSummary?.latestPromotionPackage) {
      setEvidenceMessage('No promotion review package is available for this ticket yet.');
      return;
    }

    navigation.setSelectedRunId(latestRunId);
    navigation.navigateToWorkspace('promotion-review');
  }, [evidenceSummary?.latestPromotionPackage, evidenceSummary?.latestRun?.runId, navigation]);

  useEffect(() => {
    if (selectedTicket?.id) {
      void refreshEvidence();
    } else {
      setEvidenceSummary(null);
      setEvidenceStatus('idle');
      setEvidenceMessage('Execution evidence has not been loaded for this ticket.');
    }
  }, [refreshEvidence, selectedTicket?.id]);

  const onSignIn = useCallback(async () => {
    await session.signIn({ email: session.email.trim(), password: session.password });
    await project.refreshProjectContext();
  }, [project, session]);

  const state: TicketsWorkspaceState = {
    apiStatus: session.apiStatus,
    accessStatus,
    apiBaseUrl: session.config.apiBaseUrl,
    projectId: selectedProjectId,
    projectStatus: projectStatus === 'fallback' ? 'fallback' : projectStatus === 'selected' ? 'selected' : 'missing',
    tokenConfigured,
    projectBadgeStatus: projectSelectionMode === 'api' ? 'selected' : projectSelectionMode === 'fallback-config' ? 'fallback' : 'missing',
    projectAccessBlocked: productAccessBlocked,
    authLabel: tokenConfigured ? 'Token rejected' : 'Missing token',
    tenants: project.tenants,
    projects: project.projects,
    selectedTenantId,
    selectedProjectId,
    tickets,
    selectedTicket,
    ticketDetailStatus,
    ticketDetailMessage,
    readiness,
    readinessStatus,
    readinessMessage,
    evidenceSummary,
    evidenceStatus,
    evidenceMessage,
    runReview,
    runReviewStatus,
    runReviewMessage,
    isRunReviewOpen,
    reviewLatestRunBlockedReason,
    startDisposableRunBlockedReason,
    implementationPlan,
    planStatus,
    planMessage,
    isEditingTicket,
    editDraft,
    saveStatus,
    saveMessage,
    isEditDirty,
    editValidationMessage,
    editBlockedReason: ticketActionBlockedReason,
    isCreatePanelOpen,
    createDraft,
    createStatus,
    createMessage,
    createBlockedReason,
    createdTicketId,
    selectedTicketId: selectedTicketIdForList,
    ticketMessage,
    tokenDraft: session.tokenDraft,
    email: session.email,
    password: session.password,
    isTokenConfigOpen: session.isTokenEditorOpen,
    isBusy,
    errorMessage: session.errorMessage
  };

  return {
    ...state,
    createTicket,
    refresh,
    actions: {
      onSelectTicket: handleSelectTicket,
      onEditTicket: beginEditTicket,
      onEditDraftChange: setEditDraft,
      onSaveTicket: saveTicket,
      onCancelEditTicket: cancelEditTicket,
      onRefreshPlan: refreshImplementationPlan,
      onRefreshReadiness: refreshReadiness,
      onRefreshEvidence: refreshEvidence,
      onStartDisposableRun: onStartDisposableRun,
      onReviewLatestRun: onReviewLatestRun,
      onRefreshRunReview,
      onDismissRunReview,
      onOpenPromotionReview: onOpenPromotionReview,
      onCreateDraftChange: setCreateDraft,
      onSubmitCreateTicket: createTicket,
      onCancelCreateTicket: closeCreatePanel,
      onConfigureToken: () => session.setTokenEditorOpen(!session.isTokenEditorOpen),
      onRetry: () => void refresh(),
      onTokenDraftChange: session.setTokenDraft,
      onEmailChange: session.setEmail,
      onPasswordChange: session.setPassword,
      onSaveToken: () => {
        session.saveToken();
        if (session.tokenDraft.length === 0) {
          project.setProjectAccessStatus('authRequired');
        }
      },
      onSignIn,
      onSelectTenant: project.selectTenantContext,
      onSelectProject: project.selectProjectContext,
      onOpenCreate: openCreatePanel,
      onRefreshTickets: refresh
    }
  };
}

function getCreateTicketBlocker(
  apiStatus: ApiStatus,
  accessStatus: ProductAccessStatus,
  tokenConfigured: boolean,
  selectedTenantId: number | null,
  selectedProjectId: number | null,
  projectSelectionMode: 'api' | 'fallback-config' | 'missing'
) {
  if (apiStatus.status === 'disconnected') {
    return 'IronDev.Api is offline. Start the backend before creating tickets.';
  }

  if (apiStatus.status !== 'connected') {
    return 'IronDev.Api is not ready yet. Retry the connection before creating tickets.';
  }

  if (!tokenConfigured || accessStatus === 'authRequired' || accessStatus === 'authInvalid') {
    return 'Sign in or configure a valid token before creating IronDev tickets.';
  }

  if (!selectedTenantId || accessStatus === 'tenantRequired') {
    return 'Select a tenant before creating IronDev tickets.';
  }

  if (!selectedProjectId || accessStatus === 'projectRequired') {
    return 'Select a project before creating IronDev tickets.';
  }

  if (projectSelectionMode === 'fallback-config') {
    return 'Select a project explicitly before creating tickets. Fallback project context is read-only.';
  }

  if (accessStatus === 'apiError' || accessStatus === 'apiOffline') {
    return 'Resolve the current API state before creating tickets.';
  }

  return null;
}

function getTicketActionBlocker(
  apiStatus: ApiStatus,
  accessStatus: ProductAccessStatus,
  tokenConfigured: boolean,
  selectedTenantId: number | null,
  selectedProjectId: number | null,
  projectSelectionMode: 'api' | 'fallback-config' | 'missing',
  selectedTicketId: number | null
) {
  const sessionBlocker = getCreateTicketBlocker(
    apiStatus,
    accessStatus,
    tokenConfigured,
    selectedTenantId,
    selectedProjectId,
    projectSelectionMode
  );

  if (sessionBlocker) {
    return sessionBlocker;
  }

  if (!selectedTicketId) {
    return 'Select a ticket before using ticket workflow actions.';
  }

  return null;
}

function getTicketEvidenceBlockedActions(args: {
  readinessStatus: TicketReadinessLoadStatus;
  readiness: BuildReadinessResult | null;
  latestRelatedRun?: LinkedRunSummary | null;
}): string[] {
  const blockedActions: string[] = [];

  if (args.readinessStatus !== 'loaded') {
    blockedActions.push('Build readiness has not been refreshed.');
  }

  if (args.readiness && !args.readiness.isReady) {
    blockedActions.push(args.readiness.message ?? 'Build readiness is not ready.');
  }

  if (!args.latestRelatedRun) {
    blockedActions.push('No execution run is linked to this ticket yet.');
  }

  return blockedActions;
}

function getLinkedDocumentCount(ticket: ProjectTicket) {
  return ticket.sourceDocumentVersionId ? 1 : 0;
}

function getLinkedTraceCount(ticket: ProjectTicket) {
  return Number(Boolean(ticket.sourceChatSessionId)) + Number(Boolean(ticket.sourceChatMessageId));
}

function mapRunStatus(run: Pick<RunReportSummary, 'status' | 'recommendation'>): LinkedRunStatus {
  const value = `${run.status ?? ''} ${run.recommendation ?? ''}`.toLowerCase();

  if (value.includes('running')) {
    return 'running';
  }

  if (value.includes('blocked')) {
    return 'blocked';
  }

  if (value.includes('human') || value.includes('review')) {
    return 'needsHumanReview';
  }

  if (value.includes('fail') || value.includes('error') || value.includes('warning')) {
    return 'failed';
  }

  if (value.includes('pass') || value.includes('succ') || value.includes('done')) {
    return 'passed';
  }

  return 'unknown';
}

function mapRunSummary(run: RunReportSummary): LinkedRunSummary {
  return {
    runId: run.runId ?? 'unknown-run',
    traceId: run.traceId ?? null,
    title: run.title ?? null,
    status: mapRunStatus(run),
    recommendation: run.recommendation ?? null,
    startedUtc: run.startedUtc ?? null,
    completedUtc: run.completedUtc ?? null
  };
}

function mapPromotionSummary(
  review: {
    packageId?: string | null;
    proposedChangeId?: string | null;
    approvalState?: string | null;
    recommendation?: string | null;
    runtimeProfileId?: string | null;
    targetLanguage?: string | null;
    promotableFileCount?: number | null;
    blockedFileCount?: number | null;
    realRepoMutationCount?: number | null;
  },
  sourceRunId: string | null,
  run: { realRepoMutationCount?: number | null } | null = null
): LinkedPromotionPackageSummary {
  return {
    packageId: review.packageId ?? null,
    proposedChangeId: review.proposedChangeId ?? null,
    approvalState: review.approvalState ?? null,
    recommendation: review.recommendation ?? null,
    runtimeProfile: review.runtimeProfileId ?? null,
    targetLanguage: review.targetLanguage ?? null,
    filesToPromoteCount: review.promotableFileCount ?? null,
    filesBlockedCount: review.blockedFileCount ?? null,
    activeRepoMutationCount: (run?.realRepoMutationCount ?? null) as number | null,
    sourceRunId
  };
}

function getLatestTicketRelatedRun(runs: RunReportSummary[], ticket: ProjectTicket): RunReportSummary | null {
  const related = runs.find((run) => {
    if (!run || !ticket?.id) {
      return false;
    }

    const sourceEntityType = (run as { sourceEntityType?: string | null }).sourceEntityType;
    const sourceEntityId = (run as { sourceEntityId?: number | null }).sourceEntityId;
    if (typeof sourceEntityId !== 'number' || sourceEntityId <= 0) {
      return false;
    }

    if (sourceEntityId !== ticket.id) {
      return false;
    }

    if (typeof sourceEntityType !== 'string' || sourceEntityType.length === 0) {
      return false;
    }

    const normalizedSourceType = sourceEntityType.toLowerCase();
    return normalizedSourceType === 'ticket' || normalizedSourceType === 'projectticket';
  });

  return related ?? null;
}

function buildTicketEvidenceSummary(args: {
  ticket: ProjectTicket;
  readiness: BuildReadinessResult | null;
  readinessStatus: TicketReadinessLoadStatus;
  latestRelatedRun?: LinkedRunSummary | null;
  latestPromotionPackage?: LinkedPromotionPackageSummary | null;
}): Omit<TicketEvidenceSummary, 'latestRun' | 'latestPromotionPackage'> {
  const linkedTraceCount = getLinkedTraceCount(args.ticket);
  const linkedDocumentCount = getLinkedDocumentCount(args.ticket);
  const blockedActions = getTicketEvidenceBlockedActions({
    readinessStatus: args.readinessStatus,
    readiness: args.readiness,
    latestRelatedRun: args.latestRelatedRun
  });

  return {
    ticketId: args.ticket.id ?? 0,
    status: 'loaded',
    message: args.latestRelatedRun ? 'Execution evidence is available for this ticket.' : 'No linked execution evidence is available yet.',
    linkedTraceCount,
    linkedDocumentCount,
    linkedDecisionCount: 0,
    linkedRunCount: args.latestRelatedRun ? 1 : 0,
    hasBlockingWarnings: blockedActions.length > 0,
    blockedActions,
    nextSafeAction: args.latestRelatedRun
      ? 'Review latest run'
      : args.readiness?.isReady
        ? 'Start disposable run'
        : 'Refresh build readiness'
  };
}

function draftFromTicket(ticket: ProjectTicket): TicketEditDraft {
  return {
    title: ticket.title ?? '',
    summary: ticket.summary ?? '',
    problem: ticket.problem ?? ticket.background ?? '',
    proposedChange: ticket.content ?? '',
    type: ticket.ticketType ?? 'Feature / Workflow',
    priority: ticket.priority ?? 'Medium',
    acceptanceCriteria: ticket.acceptanceCriteria ?? '',
    technicalNotes: ticket.technicalNotes ?? '',
    unitTests: ticket.unitTests ?? '',
    integrationTests: ticket.integrationTests ?? '',
    manualTests: ticket.manualTests ?? '',
    regressionTests: ticket.regressionTests ?? '',
    buildValidation: ticket.buildValidation ?? ''
  };
}

function buildTicketFromEditDraft(ticket: ProjectTicket, draft: TicketEditDraft, projectId: number): ProjectTicket {
  return {
    ...ticket,
    projectId,
    title: draft.title.trim(),
    summary: nullIfBlank(draft.summary),
    problem: nullIfBlank(draft.problem),
    content: nullIfBlank(draft.proposedChange),
    ticketType: nullIfBlank(draft.type),
    priority: nullIfBlank(draft.priority),
    acceptanceCriteria: nullIfBlank(draft.acceptanceCriteria),
    technicalNotes: nullIfBlank(draft.technicalNotes),
    unitTests: nullIfBlank(draft.unitTests),
    integrationTests: nullIfBlank(draft.integrationTests),
    manualTests: nullIfBlank(draft.manualTests),
    regressionTests: nullIfBlank(draft.regressionTests),
    buildValidation: nullIfBlank(draft.buildValidation)
  };
}

function validateEditDraft(draft: TicketEditDraft) {
  if (!draft.title.trim()) {
    return 'Title is required before saving a ticket.';
  }

  return null;
}

function areEditDraftsEqual(left: TicketEditDraft, right: TicketEditDraft) {
  return JSON.stringify(normalizeEditDraft(left)) === JSON.stringify(normalizeEditDraft(right));
}

function normalizeEditDraft(draft: TicketEditDraft) {
  return {
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    problem: draft.problem.trim(),
    proposedChange: draft.proposedChange.trim(),
    type: draft.type.trim(),
    priority: draft.priority.trim(),
    acceptanceCriteria: normalizeMultiline(draft.acceptanceCriteria),
    technicalNotes: draft.technicalNotes.trim(),
    unitTests: draft.unitTests.trim(),
    integrationTests: draft.integrationTests.trim(),
    manualTests: draft.manualTests.trim(),
    regressionTests: draft.regressionTests.trim(),
    buildValidation: draft.buildValidation.trim()
  };
}

function normalizeMultiline(value: string) {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .join('\n');
}

function nullIfBlank(value: string) {
  const trimmed = value.trim();

  return trimmed.length > 0 ? trimmed : null;
}

function splitAcceptanceCriteria(value: string) {
  const items = value
    .split(/\r?\n/)
    .map((item) => item.replace(/^[-*]\s*/, '').trim())
    .filter(Boolean);

  return items.length > 0 ? items : undefined;
}
