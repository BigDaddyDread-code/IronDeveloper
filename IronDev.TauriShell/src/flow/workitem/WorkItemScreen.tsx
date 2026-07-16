import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import type {
  AcceptedApprovalApiError,
  BuildReadinessResult,
  ProjectWorkItemReadModel,
  ProjectMemberDirectoryResponse,
  ProjectTicket,
  SkeletonCriticPackage,
  SkeletonCriticReviewOutcome,
  SkeletonRunReport,
  TicketEvidenceSummary,
  TicketBuildRunDto
} from '../../api/types';
import { IronDevApiError } from '../../api/ironDevApi';
import { MarkdownRenderer } from '../../components/MarkdownRenderer';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { ContractRail } from '../components/ContractRail';
import { StageRail } from '../components/StageRail';
import { GateInfo, ShapeDraft, WorkItemStage, emptyShapeDraft } from '../flowTypes';
import { BuildStage, statusTone } from './BuildStage';
import { ReviewStage } from './ReviewStage';
import { runAgentRoleLabel } from '../runReadiness';

interface WorkItemScreenProps {
  ticket: ProjectTicket | null;
  onTicketCreated: (ticket: ProjectTicket) => void;
  onBackToBoard: () => void;
  onOpenGovernanceLibrary: () => void;
  onDiscussInChat: (sessionId?: number | null) => void;
  onConfigureRunAgents: () => void;
  onConfigureProjectWorkConnection: () => void;
}

interface DiscussionEntry {
  id: string;
  role: 'user' | 'assistant';
  text: string;
}

let entrySeq = 0;
function nextEntryId(): string {
  entrySeq += 1;
  return `m-${entrySeq}`;
}

function draftFromTicket(ticket: ProjectTicket): ShapeDraft {
  const criteria = (ticket.acceptanceCriteria ?? '')
    .split('\n')
    .map((line) => line.replace(/^[-*]\s*/, '').trim())
    .filter((line) => line.length > 0)
    .map((line, index) => ({ id: `c-${index}`, text: line, confirmed: true }));

  const architectureRefs = (ticket.linkedFilePaths ?? '')
    .split(/[\n,;]/)
    .map((p) => p.trim())
    .filter((p) => p.length > 0)
    .slice(0, 4)
    .map((p) => `file · ${p}`);

  return {
    title: ticket.title ?? '',
    summary: ticket.summary ?? '',
    criteria,
    openQuestions: [],
    architectureRefs
  };
}

function runFromReport(report: SkeletonRunReport): TicketBuildRunDto {
  return {
    runId: report.runId,
    projectId: report.projectId,
    ticketId: report.ticketId,
    status: report.status,
    currentNode: 'SkeletonRun',
    requiresHumanApproval: report.status === 'PausedForApproval',
    message: report.summary
  };
}

function stageFromProjection(stage: string | null | undefined): WorkItemStage {
  switch (stage?.toLowerCase()) {
    case 'shape':
      return 'shape';
    case 'build':
      return 'build';
    case 'review':
      return 'review';
    case 'done':
      return 'done';
    default:
      return 'ticket';
  }
}

function applyRecoveryStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'ApplyRefused':
      return 'Apply refused';
    case 'ApplyInProgress':
      return 'Apply in progress';
    case 'RecoveryEvidenceMissing':
      return 'Recovery evidence missing';
    case 'RetryReady':
      return 'Safe retry available';
    case 'Interrupted':
      return 'Attempt interrupted';
    case 'ManualReviewRequired':
      return 'Manual review required';
    case 'Abandoned':
      return 'Attempt abandoned';
    case 'Applied':
      return 'Applied';
    default:
      return 'Not required';
  }
}

function executionProofStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'InProgress':
      return 'Execution in progress';
    case 'ProofMissing':
      return 'Execution proof missing';
    case 'ExecutionObserved':
      return 'Execution observed';
    case 'LoopVerified':
      return 'Governed loop verified';
    default:
      return 'No run';
  }
}

export function WorkItemScreen({
  ticket,
  onTicketCreated,
  onBackToBoard,
  onOpenGovernanceLibrary,
  onDiscussInChat,
  onConfigureRunAgents,
  onConfigureProjectWorkConnection
}: WorkItemScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();

  const [stage, setStage] = useState<WorkItemStage>(ticket ? 'ticket' : 'shape');
  const [draft, setDraft] = useState<ShapeDraft>(ticket ? draftFromTicket(ticket) : emptyShapeDraft());
  const [discussion, setDiscussion] = useState<DiscussionEntry[]>([]);
  const [prompt, setPrompt] = useState('');
  const [isThinking, setIsThinking] = useState(false);
  const [isPromoting, setIsPromoting] = useState(false);
  const [readiness, setReadiness] = useState<BuildReadinessResult | null>(null);
  const [evidenceSummary, setEvidenceSummary] = useState<TicketEvidenceSummary | null>(null);
  const [evidenceLoadState, setEvidenceLoadState] = useState<'idle' | 'loading' | 'ready' | 'empty' | 'error'>('idle');
  const [evidenceErrorMessage, setEvidenceErrorMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [run, setRun] = useState<TicketBuildRunDto | null>(null);
  const [report, setReport] = useState<SkeletonRunReport | null>(null);
  const [criticPackage, setCriticPackage] = useState<SkeletonCriticPackage | null>(null);
  const [isStartingRun, setIsStartingRun] = useState(false);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [gateNotice, setGateNotice] = useState<string | null>(null);
  const [criticOutcome, setCriticOutcome] = useState<SkeletonCriticReviewOutcome | null>(null);
  const [workItem, setWorkItem] = useState<ProjectWorkItemReadModel | null>(null);
  const [workItemLoadState, setWorkItemLoadState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle');
  const [workItemLoadError, setWorkItemLoadError] = useState<string | null>(null);
  const [memberDirectory, setMemberDirectory] = useState<ProjectMemberDirectoryResponse | null>(null);
  const [collaborationEditing, setCollaborationEditing] = useState(false);
  const [assigneeDraft, setAssigneeDraft] = useState('');
  const [waitingOnDraft, setWaitingOnDraft] = useState('');
  const [followerDraft, setFollowerDraft] = useState<number[]>([]);
  const [collaborationError, setCollaborationError] = useState<string | null>(null);
  const [collaborationBusy, setCollaborationBusy] = useState(false);
  const [applyRecoveryReason, setApplyRecoveryReason] = useState('');

  useEffect(() => {
    setStage(ticket ? 'ticket' : 'shape');
    setDraft(ticket ? draftFromTicket(ticket) : emptyShapeDraft());
    setReadiness(null);
    setEvidenceSummary(null);
    setEvidenceLoadState(ticket ? 'loading' : 'idle');
    setEvidenceErrorMessage(null);
    setErrorMessage(null);
    setRun(null);
    setReport(null);
    setCriticPackage(null);
    setCriticOutcome(null);
    setGateNotice(null);
    setWorkItem(null);
    setWorkItemLoadState(ticket ? 'loading' : 'idle');
    setWorkItemLoadError(null);
    setMemberDirectory(null);
    setCollaborationEditing(false);
    setCollaborationError(null);
    setApplyRecoveryReason('');
  }, [ticket?.id]);

  const refreshWorkItemProjection = useCallback(async (signal?: AbortSignal, syncStage = true) => {
    if (project.selectedProjectId === null || ticket?.id === undefined) {
      return null;
    }

    setWorkItemLoadState('loading');
    setWorkItemLoadError(null);
    try {
      const next = await session.client.getProjectWorkItem(project.selectedProjectId, ticket.id, signal);
      setWorkItem(next);
      setAssigneeDraft(next.collaboration.assignee?.userId ? String(next.collaboration.assignee.userId) : '');
      setWaitingOnDraft(next.collaboration.waitingOn?.userId
        ? `user:${next.collaboration.waitingOn.userId}`
        : next.collaboration.waitingOn?.displayName ? `role:${next.collaboration.waitingOn.displayName}` : '');
      setFollowerDraft((next.collaboration.followers ?? []).flatMap((follower) => follower.userId ? [follower.userId] : []));
      if (syncStage) {
        setStage(stageFromProjection(next.stage));
      }
      setWorkItemLoadState('ready');
      return next;
    } catch (error: unknown) {
      if (signal?.aborted) {
        return null;
      }
      setWorkItem(null);
      setWorkItemLoadState('error');
      setWorkItemLoadError(error instanceof Error ? error.message : 'The Work Item projection could not be loaded.');
      return null;
    }
  }, [project.selectedProjectId, session.client, ticket?.id]);

  useEffect(() => {
    if (!ticket || ticket.id === undefined) {
      return;
    }
    const controller = new AbortController();
    void refreshWorkItemProjection(controller.signal);
    return () => controller.abort();
  }, [refreshWorkItemProjection, ticket]);

  useEffect(() => {
    if (!ticket || project.selectedProjectId === null) return;
    const controller = new AbortController();
    session.client.getProjectMembers(project.selectedProjectId, controller.signal)
      .then(setMemberDirectory)
      .catch(() => { if (!controller.signal.aborted) setMemberDirectory(null); });
    return () => controller.abort();
  }, [project.selectedProjectId, session.client, ticket]);

  const saveCollaboration = async () => {
    if (!ticket?.id || project.selectedProjectId === null) return;
    const waitingUserId = waitingOnDraft.startsWith('user:') ? Number(waitingOnDraft.slice(5)) : null;
    const waitingLabel = waitingOnDraft.startsWith('role:') ? waitingOnDraft.slice(5) : null;
    setCollaborationBusy(true);
    setCollaborationError(null);
    try {
      await session.client.setProjectWorkItemCollaboration(project.selectedProjectId, ticket.id, {
        expectedRevision: workItem?.collaboration.revision ?? 0,
        assigneeUserId: assigneeDraft ? Number(assigneeDraft) : null,
        followerUserIds: followerDraft,
        waitingOnUserId: waitingUserId,
        waitingOnKind: waitingLabel ? 'Role' : waitingUserId ? 'Human' : null,
        waitingOnLabel: waitingLabel
      });
      await refreshWorkItemProjection(undefined, false);
      setCollaborationEditing(false);
    } catch (error) {
      if (error instanceof IronDevApiError && error.status === 409) {
        const body = error.body as { code?: string; expectedRevision?: number; currentRevision?: number; nextSafeAction?: string } | null;
        setCollaborationError(body?.code === 'StaleWrite'
          ? `Stale write refused. Attempted version ${body.expectedRevision}; current version ${body.currentRevision}. ${body.nextSafeAction ?? 'Reload and compare.'}`
          : 'Ownership conflict. Reload and compare before saving again.');
      } else {
        setCollaborationError(error instanceof Error ? error.message : 'Ownership was not changed.');
      }
    } finally {
      setCollaborationBusy(false);
    }
  };

  const hasUndispositionedFindings = useMemo(() => {
    const reviews = report?.criticReviews ?? [];
    const dispositioned = new Set((report?.findingDispositions ?? []).map((disposition) => disposition.findingId));
    return reviews.flatMap((review) => review.findingIds).some((findingId) => !dispositioned.has(findingId));
  }, [report]);

  useEffect(() => {
    if (!ticket || project.selectedProjectId === null || ticket.id === undefined) {
      return;
    }
    const controller = new AbortController();
    session.client
      .getTicketBuildReadiness(project.selectedProjectId, ticket.id, controller.signal)
      .then(setReadiness)
      .catch(() => {
        if (!controller.signal.aborted) {
          setReadiness(null);
        }
      });
    return () => controller.abort();
  }, [session.client, project.selectedProjectId, ticket]);

  useEffect(() => {
    const selectedProjectId = project.selectedProjectId;
    const selectedTicketId = ticket?.id;

    if (!ticket || selectedProjectId === null || selectedTicketId === undefined) {
      return;
    }

    const projectId: number = selectedProjectId;
    const ticketId: number = selectedTicketId;
    const controller = new AbortController();
    setEvidenceLoadState('loading');
    setEvidenceErrorMessage(null);

    async function hydrateLinkedRun() {
      try {
        const summary = await session.client.getTicketEvidenceSummary(projectId, ticketId, controller.signal);
        if (controller.signal.aborted) {
          return;
        }
        setEvidenceSummary(summary);

        const latestRun = summary.latestRun;
        if (!latestRun?.runId) {
          setEvidenceLoadState('empty');
          return;
        }

        setRun({
          runId: latestRun.runId,
          projectId,
          ticketId,
          status: latestRun.status,
          currentNode: 'LinkedRunEvidence',
          requiresHumanApproval: latestRun.status === 'needsHumanReview',
          message: latestRun.recommendation ?? summary.message
        });
        try {
          const nextReport = await session.client.getSkeletonRunReport(projectId, ticketId, latestRun.runId, controller.signal);
          if (!controller.signal.aborted) {
            setReport(nextReport);
            setRun(runFromReport(nextReport));
          }
        } catch {
          if (!controller.signal.aborted) {
            setReport(null);
          }
        }

        try {
          const nextPackage = await session.client.getSkeletonCriticPackage(
            projectId,
            ticketId,
            latestRun.runId,
            controller.signal
          );
          if (!controller.signal.aborted) {
            setCriticPackage(nextPackage);
          }
        } catch {
          if (!controller.signal.aborted) {
            setCriticPackage(null);
          }
        }

        if (!controller.signal.aborted) {
          setEvidenceLoadState('ready');
        }
      } catch (error: unknown) {
        if (controller.signal.aborted) {
          return;
        }
        setEvidenceSummary(null);
        setEvidenceErrorMessage(error instanceof Error ? error.message : 'Linked run evidence could not be loaded.');
        setEvidenceLoadState('error');
      }
    }

    void hydrateLinkedRun();
    return () => controller.abort();
  }, [session.client, project.selectedProjectId, ticket]);

  const unresolvedQuestions = draft.openQuestions.filter((q) => !q.resolved);
  const unconfirmedCriteria = draft.criteria.filter((c) => !c.confirmed);

  const shapeBlockers = useMemo(() => {
    const blockers: string[] = [];
    if (draft.title.trim().length === 0) {
      blockers.push('the work item needs a title');
    }
    if (draft.criteria.length === 0) {
      blockers.push('no acceptance criteria yet');
    }
    if (unconfirmedCriteria.length > 0) {
      blockers.push(`${unconfirmedCriteria.length} unconfirmed criterion${unconfirmedCriteria.length > 1 ? 'a' : ''}`);
    }
    if (unresolvedQuestions.length > 0) {
      blockers.push(`${unresolvedQuestions.length} open question${unresolvedQuestions.length > 1 ? 's' : ''}`);
    }
    return blockers;
  }, [draft.title, draft.criteria.length, unconfirmedCriteria.length, unresolvedQuestions.length]);

  const gates: GateInfo[] = useMemo(() => {
    if (workItem) {
      const state = workItem.gate.state?.toLowerCase() ?? 'blocked';
      return [
        {
          afterStage: stageFromProjection(workItem.stage),
          label: `${state} gate`,
          state: state === 'blocked' ? 'locked' : 'open',
          detail: workItem.gate.reason ?? undefined
        }
      ];
    }
    if (stage === 'shape') {
      return [
        {
          afterStage: 'shape',
          label: 'readiness',
          state: shapeBlockers.length === 0 ? 'open' : 'locked',
          detail: shapeBlockers.length > 0 ? shapeBlockers.join('; ') : undefined
        },
        { afterStage: 'ticket', label: 'approval', state: 'locked', detail: 'a governed run must halt at the gate first' }
      ];
    }
    const readinessBlockers = (readiness?.blockingIssues ?? []).filter(Boolean) as string[];
    return [
      { afterStage: 'shape', label: 'ready', state: 'open' },
      {
        afterStage: 'ticket',
        label: 'readiness',
        state: readiness?.isReady ? 'open' : 'locked',
        detail: readiness?.isReady
          ? undefined
          : readinessBlockers.length > 0
            ? readinessBlockers.join('; ')
            : (readiness?.message ?? 'readiness not yet evaluated')
      },
      {
        afterStage: 'build',
        label: 'findings',
        state: hasUndispositionedFindings ? 'locked' : 'open',
        detail: hasUndispositionedFindings ? 'critic findings await human dispositions' : undefined
      },
      {
        afterStage: 'review',
        label: 'human gate',
        state: report?.approval?.continuationUnblocked === true ? 'open' : 'locked',
        detail:
          report?.approval?.continuationUnblocked === true
            ? undefined
            : 'continuation has not consumed a live accepted approval'
      }
    ];
  }, [
    stage,
    shapeBlockers,
    readiness?.isReady,
    readiness?.blockingIssues,
    readiness?.message,
    hasUndispositionedFindings,
    report?.approval?.continuationUnblocked,
    workItem
  ]);

  const sendPrompt = useCallback(
    async (event: FormEvent) => {
      event.preventDefault();
      const text = prompt.trim();
      if (text.length === 0 || project.selectedProjectId === null || isThinking) {
        return;
      }
      setPrompt('');
      setErrorMessage(null);
      setDiscussion((prev) => [...prev, { id: nextEntryId(), role: 'user', text }]);
      setDraft((prev) => (prev.title.trim().length === 0 ? { ...prev, title: text.slice(0, 80), summary: text } : prev));
      setIsThinking(true);
      try {
        const response = await session.client.completeChat(project.selectedProjectId, {
          prompt: `You are helping a business analyst shape a work item. Requirement so far: "${draft.title || text}". New input: "${text}". Reply briefly for a shaping discussion.`,
          mode: 'discussion'
        });
        const reply = response.response ?? 'No response.';
        setDiscussion((prev) => [...prev, { id: nextEntryId(), role: 'assistant', text: reply }]);
        if (response.linkedFilePaths) {
          const refs = response.linkedFilePaths
            .split(/[\n,;]/)
            .map((p) => p.trim())
            .filter((p) => p.length > 0)
            .slice(0, 3)
            .map((p) => `file · ${p}`);
          if (refs.length > 0) {
            setDraft((prev) => ({
              ...prev,
              architectureRefs: Array.from(new Set([...prev.architectureRefs, ...refs]))
            }));
          }
        }
      } catch (error: unknown) {
        setErrorMessage(error instanceof Error ? error.message : 'Workshop request failed.');
      } finally {
        setIsThinking(false);
      }
    },
    [prompt, project.selectedProjectId, session.client, draft.title, isThinking]
  );

  const addCriterion = useCallback((text: string) => {
    const trimmed = text.trim();
    if (trimmed.length === 0) {
      return;
    }
    setDraft((prev) => ({
      ...prev,
      criteria: [...prev.criteria, { id: `c-${prev.criteria.length}-${Date.now().toString(36)}`, text: trimmed, confirmed: false }]
    }));
  }, []);

  const promoteToTicket = useCallback(async () => {
    if (project.selectedProjectId === null || shapeBlockers.length > 0 || isPromoting) {
      return;
    }
    setIsPromoting(true);
    setErrorMessage(null);
    try {
      const created = await session.client.createProjectTicket(project.selectedProjectId, {
        title: draft.title,
        type: 'Feature',
        priority: 'Medium',
        summary: draft.summary || draft.title,
        problem: draft.summary || draft.title,
        proposedChange: draft.criteria.map((c) => c.text).join('\n'),
        acceptanceCriteria: draft.criteria.map((c) => c.text)
      });
      setStage('ticket');
      onTicketCreated(created);
    } catch (error: unknown) {
      setErrorMessage(error instanceof Error ? error.message : 'Could not create the ticket.');
    } finally {
      setIsPromoting(false);
    }
  }, [project.selectedProjectId, shapeBlockers.length, isPromoting, session.client, draft, onTicketCreated]);

  const [criterionInput, setCriterionInput] = useState('');

  // ── P0-7: Build and Review consume the walking-skeleton loop. Every action is
  // a request to a governed endpoint; the backend verifies and refuses. ──

  const describeApiError = (error: unknown, fallback: string): string => {
    if (error instanceof IronDevApiError) {
      const body = error.body as { errors?: AcceptedApprovalApiError[] } | undefined;
      const detail = body?.errors?.map((issue) => issue.message).join(' ');
      return detail && detail.length > 0 ? detail : error.message;
    }
    return error instanceof Error ? error.message : fallback;
  };

  const refreshRunEvidence = useCallback(
    async (activeRun: TicketBuildRunDto) => {
      if (project.selectedProjectId === null || ticket?.id === undefined) {
        return;
      }
      try {
        const nextReport = await session.client.getSkeletonRunReport(project.selectedProjectId, ticket.id, activeRun.runId);
        setReport(nextReport);
      } catch {
        setReport(null);
      }
      try {
        const nextPackage = await session.client.getSkeletonCriticPackage(project.selectedProjectId, ticket.id, activeRun.runId);
        setCriticPackage(nextPackage);
      } catch {
        setCriticPackage(null);
      }
    },
    [session.client, project.selectedProjectId, ticket]
  );

  const startRun = useCallback(async (purpose: 'ProjectFeatureWork' | 'SmokeSimulation') => {
    if (project.selectedProjectId === null || ticket?.id === undefined || isStartingRun) {
      return;
    }
    setIsStartingRun(true);
    setErrorMessage(null);
    setGateNotice(null);
    try {
      const started = await session.client.startSkeletonRun(project.selectedProjectId, ticket.id, purpose);
      setRun(started);
      setStage('build');
      if (purpose === 'SmokeSimulation') {
        setGateNotice('Workflow smoke test started with the fixed LocalTest fixture. This run exercises the governed corridor; it does not implement the Work Item.');
      }
      await refreshRunEvidence(started);
      await refreshWorkItemProjection(undefined, false);
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'Could not start the build run.'));
    } finally {
      setIsStartingRun(false);
    }
  }, [project.selectedProjectId, ticket, isStartingRun, session.client, refreshRunEvidence, refreshWorkItemProjection]);

  const startBuildRun = useCallback(
    () => startRun('ProjectFeatureWork'),
    [startRun]
  );

  const startWorkflowSmokeRun = useCallback(
    () => startRun('SmokeSimulation'),
    [startRun]
  );

  const requestCriticReview = useCallback(async () => {
    if (project.selectedProjectId === null || ticket?.id === undefined || run === null || busyAction !== null) {
      return;
    }
    setBusyAction('critic');
    setErrorMessage(null);
    try {
      const outcome = await session.client.requestSkeletonCriticReview(project.selectedProjectId, ticket.id, run.runId);
      setCriticOutcome(outcome);
      if (!outcome.succeeded) {
        setGateNotice(outcome.failureReason);
      }
      await refreshRunEvidence(run);
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The critic review request failed.'));
    } finally {
      setBusyAction(null);
    }
  }, [project.selectedProjectId, ticket, run, busyAction, session.client, refreshRunEvidence]);

  const recordDisposition = useCallback(
    async (findingId: string, disposition: string, reason: string) => {
      if (project.selectedProjectId === null || ticket?.id === undefined || run === null || busyAction !== null) {
        return;
      }
      setBusyAction('disposition');
      setErrorMessage(null);
      try {
        const outcome = await session.client.recordFindingDisposition(
          project.selectedProjectId,
          ticket.id,
          run.runId,
          findingId,
          disposition,
          reason
        );
        if (!outcome.succeeded) {
          setErrorMessage(outcome.failureReason);
        }
        await refreshRunEvidence(run);
      } catch (error: unknown) {
        setErrorMessage(describeApiError(error, 'The disposition was refused.'));
      } finally {
        setBusyAction(null);
      }
    },
    [project.selectedProjectId, ticket, run, busyAction, session.client, refreshRunEvidence]
  );

  // DOGFOOD-2 finding F-L: evidence references are reference-shaped by contract —
  // the backend allows only letters, digits, '-', '_', '.', ':' (no spaces). The
  // typed reason is free text, so it is encoded to that alphabet before it rides
  // as the labeled human-reason evidence entry; anything else is refused
  // UNSUPPORTED_CHARACTERS by the real API (the mocked spec never noticed).
  const encodeHumanReasonEvidence = (reason: string): string =>
    reason
      .trim()
      .replace(/[^A-Za-z0-9\-_.:]+/g, '-')
      .replace(/-{2,}/g, '-')
      .replace(/^-+|-+$/g, '');

  const recordApproval = useCallback(async (reason: string) => {
    const requirement = report?.approval;
    if (project.selectedProjectId === null || run === null || !requirement || busyAction !== null) {
      return;
    }
    setBusyAction('record');
    setErrorMessage(null);
    try {
      const envelope = await session.client.recordAcceptedApproval(project.selectedProjectId, {
        approvalTargetKind: requirement.targetKind,
        approvalTargetId: run.runId,
        approvalTargetHash: requirement.targetHash,
        capabilityCode: requirement.capabilityCode,
        approvalPurpose: 'workflow-continuation-input',
        correlationId: run.runId,
        causationId: `critic-pkg-${run.runId}`,
        // The ceremony's typed reason rides as a labeled durable evidence entry —
        // the approval record itself says why the human approved.
        evidenceReferences: [`critic-package-sha256:${requirement.targetHash}`, `human-reason:${encodeHumanReasonEvidence(reason)}`],
        boundaryMaxims: ['Approval binds to the reviewed critic package hash.', 'Halt is not approval.']
      });
      setGateNotice(
        `Approval ${envelope.acceptedApprovalId ?? ''} recorded. Recording is not continuation — request continuation for the backend to verify it live.`
      );
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The approval record was refused.'));
    } finally {
      setBusyAction(null);
    }
  }, [report, project.selectedProjectId, run, busyAction, session.client]);

  const requestContinuation = useCallback(async () => {
    if (project.selectedProjectId === null || ticket?.id === undefined || run === null || busyAction !== null) {
      return;
    }
    setBusyAction('continue');
    setErrorMessage(null);
    try {
      const result = await session.client.requestSkeletonRunContinuation(project.selectedProjectId, ticket.id, run.runId);
      setRun(result);
      setGateNotice(result.message ?? null);
      await refreshRunEvidence(result);
      await refreshWorkItemProjection(undefined, false);
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The continuation request failed.'));
    } finally {
      setBusyAction(null);
    }
  }, [project.selectedProjectId, ticket, run, busyAction, session.client, refreshRunEvidence, refreshWorkItemProjection]);

  const requestApply = useCallback(async () => {
    if (project.selectedProjectId === null || ticket?.id === undefined || run === null || busyAction !== null) {
      return;
    }
    setBusyAction('apply');
    setErrorMessage(null);
    try {
      const result = await session.client.requestSkeletonRunApply(project.selectedProjectId, ticket.id, run.runId);
      setRun(result);
      setGateNotice(result.message ?? null);
      await refreshRunEvidence(result);
      await refreshWorkItemProjection(undefined, false);
      if (result.status === 'Applied') {
        setStage('done');
      }
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The apply request failed.'));
    } finally {
      setBusyAction(null);
    }
  }, [project.selectedProjectId, ticket, run, busyAction, session.client, refreshRunEvidence, refreshWorkItemProjection]);

  const requestApplyRecovery = useCallback(async (action: string) => {
    if (project.selectedProjectId === null || ticket?.id === undefined || run === null || busyAction !== null || !applyRecoveryReason.trim()) {
      return;
    }
    setBusyAction(`apply-recovery-${action}`);
    setErrorMessage(null);
    try {
      const result = await session.client.requestSkeletonRunApplyRecovery(
        project.selectedProjectId,
        ticket.id,
        run.runId,
        action,
        applyRecoveryReason.trim()
      );
      setRun(result);
      setGateNotice(result.message ?? null);
      setApplyRecoveryReason('');
      await refreshRunEvidence(result);
      await refreshWorkItemProjection(undefined, false);
      if (result.status === 'Applied') setStage('done');
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The apply recovery decision failed.'));
    } finally {
      setBusyAction(null);
    }
  }, [project.selectedProjectId, ticket, run, busyAction, applyRecoveryReason, session.client, refreshRunEvidence, refreshWorkItemProjection]);

  const performPrimaryAction = useCallback(() => {
    switch (workItem?.primaryAction.kind) {
      case 'StartRun':
        void startBuildRun();
        break;
      case 'ConfigureRunAgents':
        onConfigureRunAgents();
        break;
      case 'RefreshRun':
        if (run) {
          void refreshRunEvidence(run).then(() => refreshWorkItemProjection());
        } else {
          void refreshWorkItemProjection();
        }
        break;
      case 'Review':
      case 'Apply':
      case 'RecoverApply':
        setStage('review');
        break;
      case 'RepairOrRetry':
        setStage('build');
        break;
      case 'ViewOutcome':
        setStage('done');
        break;
      default:
        break;
    }
  }, [workItem?.primaryAction.kind, startBuildRun, run, refreshRunEvidence, refreshWorkItemProjection, onConfigureRunAgents]);

  if (ticket && workItemLoadState === 'loading' && workItem === null) {
    return <p className="fl-empty" data-testid="flow.workItemProjection.loading">Loading current Work Item truth...</p>;
  }

  if (ticket && workItemLoadState === 'error') {
    return (
      <section className="fl-outcome" data-testid="flow.workItemProjection.error">
        <p className="fl-eyebrow">Work Item unavailable</p>
        <h1 className="fl-h1">Current lifecycle truth could not be loaded</h1>
        <p className="fl-sub">{workItemLoadError ?? 'The backend Work Item projection returned an error.'}</p>
        <button className="fl-btn fl-pri" type="button" onClick={() => void refreshWorkItemProjection()}>
          Retry
        </button>
      </section>
    );
  }

  return (
    <div data-testid="flow.workItem">
      <div className="fl-workitem-head">
        <div>
          <p className="fl-eyebrow">{workItem?.workItemId ? `WI-${workItem.workItemId}` : 'Work Item'}</p>
          <h1 className="fl-h1">{workItem?.title ?? (draft.title.trim().length > 0 ? draft.title : 'New work item')}</h1>
          <p className="fl-sub" data-testid="flow.workItem.statusSummary">
            {workItem?.statusSummary ??
              (stage === 'shape'
                ? 'Shaping - discussion produces the contract.'
                : 'The ticket contract is the next gate input.')}
          </p>
        </div>
        <div className="fl-workitem-head-actions">
          {workItem ? (
            <div className="fl-workitem-state" data-testid="flow.workItem.state">
              <strong>{workItem.stage ?? 'Unknown stage'}</strong>
              <span>{workItem.state ?? 'Unknown state'}</span>
            </div>
          ) : null}
          <button className="fl-btn" onClick={onBackToBoard}>Back to board</button>
          {workItem ? (
            <button
              className="fl-btn fl-pri"
              type="button"
              disabled={!workItem.primaryAction.allowed}
              onClick={performPrimaryAction}
              data-testid="flow.workItem.primaryAction"
            >
              {workItem.primaryAction.label ?? 'No action available'}
            </button>
          ) : null}
        </div>
      </div>

      <StageRail activeStage={stage} gates={gates} />

      {workItem ? (
        <section
          className={`fl-workitem-gate fl-workitem-gate-${workItem.gate.state?.toLowerCase() ?? 'blocked'}`}
          data-testid="flow.workItem.gate"
        >
          <div>
            <p className="fl-plabel">{workItem.gate.state ?? 'Gate'}</p>
            <strong>{workItem.gate.reason ?? 'The backend returned no gate reason.'}</strong>
            <p>{workItem.gate.nextSafeAction ?? 'Refresh backend state before acting.'}</p>
          </div>
          {(workItem.gate.technicalDetails?.length ?? 0) > 0 ? (
            <details>
              <summary>Technical details</summary>
              <ul>{workItem.gate.technicalDetails?.map((detail) => <li key={detail}>{detail}</li>)}</ul>
            </details>
          ) : null}
        </section>
      ) : null}

      {workItem?.runReadiness?.state === 'RunConfigurationRequired' ? (
        <section className="fl-board-blocked" data-testid="flow.workItem.runReadiness">
          <div>
            <strong>Run configuration required · {workItem.runReadiness.blockedCount ?? 0} agent blockers.</strong>
            <ul>
              {workItem.runReadiness.blockers?.map((blocker) => (
                <li key={`${blocker.role}-${blocker.reasonCode}`}>{runAgentRoleLabel(blocker.role)}: {blocker.reason}</li>
              ))}
            </ul>
          </div>
          {workItem.runReadiness.blockers?.some((blocker) => blocker.reasonCode === 'RunAgentConnectionPurposeMismatch') ? (
            <div className="fl-actions">
              <button
                className="fl-btn fl-pri"
                type="button"
                onClick={onConfigureProjectWorkConnection}
                data-testid="flow.workItem.configureProjectWorkConnection"
              >
                Configure project-work connection
              </button>
              <button
                className="fl-btn"
                type="button"
                disabled={isStartingRun}
                onClick={() => void startWorkflowSmokeRun()}
                data-testid="flow.workItem.runWorkflowSmoke"
              >
                {isStartingRun ? 'Starting smoke test...' : 'Run workflow smoke test'}
              </button>
            </div>
          ) : (
            <button className="fl-btn fl-pri" type="button" onClick={onConfigureRunAgents} data-testid="flow.workItem.configureRunAgents">
              Configure run agents
            </button>
          )}
        </section>
      ) : null}

      {workItem && !['NotRequired', 'Applied'].includes(workItem.applyRecovery.status ?? '') ? (
        <section className="fl-apply-recovery" data-testid="flow.workItem.applyRecovery">
          <div className="fl-apply-recovery-head">
            <div>
              <p className="fl-plabel">Apply recovery</p>
              <strong>{workItem.applyRecovery.required ? 'Recovery evidence required' : 'Apply refused before recovery'}</strong>
            </div>
            <span>{applyRecoveryStatusLabel(workItem.applyRecovery.status)}</span>
          </div>
          <p>{workItem.applyRecovery.reason}</p>
          {workItem.applyRecovery.applyAttemptId ? (
            <dl className="fl-apply-recovery-facts">
              <div><dt>Attempt</dt><dd>{workItem.applyRecovery.applyAttemptNumber}</dd></div>
              <div><dt>Status</dt><dd>{workItem.applyRecovery.attemptStatus}</dd></div>
              <div><dt>Mutation</dt><dd>{workItem.applyRecovery.mutationState}</dd></div>
              <div><dt>Identity</dt><dd>{workItem.applyRecovery.applyAttemptId}</dd></div>
            </dl>
          ) : null}
          {workItem.applyRecovery.required ? (
            <dl className="fl-apply-recovery-facts">
              <div><dt>Succeeded stages</dt><dd>{workItem.applyRecovery.succeededStageCount ?? 0}</dd></div>
              <div><dt>Failed stages</dt><dd>{workItem.applyRecovery.failedStageCount ?? 0}</dd></div>
              <div><dt>Existing receipts</dt><dd>{workItem.applyRecovery.existingReceiptCount ?? 0}</dd></div>
              <div><dt>Missing receipts</dt><dd>{workItem.applyRecovery.missingReceiptCount ?? 0}</dd></div>
            </dl>
          ) : null}
          <p><strong>Next safe action:</strong> {workItem.applyRecovery.nextSafeAction}</p>
          {(workItem.applyRecovery.availableActions?.length ?? 0) > 0 ? (
            <div className="fl-apply-recovery-actions" data-testid="flow.workItem.applyRecovery.actions">
              <label className="fl-field">
                <span>Recovery reason</span>
                <textarea
                  value={applyRecoveryReason}
                  onChange={(event) => setApplyRecoveryReason(event.target.value)}
                  placeholder="Record why this is the safe next action."
                  data-testid="flow.workItem.applyRecovery.reason"
                />
              </label>
              <div className="fl-actions">
                {workItem.applyRecovery.availableActions?.map((action) => (
                  <button
                    className={action === 'Abandon' ? 'fl-btn fl-danger' : 'fl-btn'}
                    type="button"
                    key={action}
                    disabled={!applyRecoveryReason.trim() || busyAction !== null}
                    onClick={() => void requestApplyRecovery(action)}
                    data-testid={`flow.workItem.applyRecovery.${action}`}
                  >
                    {action === 'Resume' ? 'Resume in new attempt'
                      : action === 'Retry' ? 'Retry in new attempt'
                        : action === 'ManualReview' ? 'Record manual review'
                          : 'Abandon apply'}
                  </button>
                ))}
              </div>
            </div>
          ) : null}
          {(workItem.applyRecovery.technicalDetails?.length ?? 0) > 0 ? (
            <details>
              <summary>Failure details</summary>
              <ul>{workItem.applyRecovery.technicalDetails?.map((detail) => <li key={detail}>{detail}</li>)}</ul>
            </details>
          ) : null}
          <small>{workItem.applyRecovery.boundary}</small>
        </section>
      ) : null}

      {workItem?.executionProof.hasRunRecord ? (
        <section className="fl-execution-proof" data-testid="flow.workItem.executionProof">
          <div className="fl-execution-proof-head">
            <div>
              <p className="fl-plabel">Execution proof</p>
              <strong>{executionProofStatusLabel(workItem.executionProof.status)}</strong>
            </div>
            <span>{workItem.executionProof.loopVerified ? 'Verified' : 'Not verified'}</span>
          </div>
          <p>{workItem.executionProof.reason}</p>
          <dl className="fl-execution-proof-facts">
            <div><dt>Started</dt><dd>{workItem.executionProof.executionStarted ? 'Observed' : 'Not observed'}</dd></div>
            <div><dt>Completed</dt><dd>{workItem.executionProof.executionCompleted ? 'Observed' : 'Not observed'}</dd></div>
            <div><dt>Execution events</dt><dd>{workItem.executionProof.durableExecutionEventCount ?? 0}</dd></div>
            <div><dt>Loop verified</dt><dd>{workItem.executionProof.loopVerified ? 'Yes' : 'No'}</dd></div>
          </dl>
          {(workItem.executionProof.durableExecutionEvents?.length ?? 0) > 0 ? (
            <div className="fl-chips">
              {workItem.executionProof.durableExecutionEvents?.map((event) => <span className="fl-chip" key={event}>{event}</span>)}
            </div>
          ) : null}
          {(workItem.executionProof.gaps?.length ?? 0) > 0 ? (
            <details>
              <summary>Evidence gaps</summary>
              <ul>{workItem.executionProof.gaps?.map((gap) => <li key={gap}>{gap}</li>)}</ul>
            </details>
          ) : null}
          <p><strong>Next safe action:</strong> {workItem.executionProof.nextSafeAction}</p>
          <small>{workItem.executionProof.boundary}</small>
        </section>
      ) : null}

      {errorMessage ? <div className="fl-error">{errorMessage}</div> : null}

      <div className="fl-cols">
        <div className="fl-panel-box">
          {stage === 'build' && run !== null ? (
            <BuildStage run={run} report={report} onRefreshReport={() => run && void refreshRunEvidence(run)} />
          ) : stage === 'review' ? (
            <ReviewStage
              criticPackage={criticPackage}
              report={report}
              criticOutcome={criticOutcome}
              busyAction={busyAction}
              onRequestCriticReview={() => void requestCriticReview()}
              onRecordDisposition={(findingId, disposition, reason) => void recordDisposition(findingId, disposition, reason)}
              onRecordApproval={(reason) => void recordApproval(reason)}
              onRequestContinuation={() => void requestContinuation()}
              onRequestApply={() => void requestApply()}
            />
          ) : stage === 'done' ? (
            <>
              <p className="fl-plabel">Final report</p>
              {report === null ? (
                <p className="fl-empty" data-testid="flow.done.reportMissing">
                  Final report not loaded. Next safe action: refresh from the backend run report endpoint or open Governance
                  Library for the recorded evidence.
                </p>
              ) : (
                <>
                  <p style={{ fontSize: 13.5, marginTop: 0 }} data-testid="flow.done.report">
                    <span style={{ color: statusTone(report.status), fontWeight: 600 }}>{report.status}</span>
                    {' · '}
                    {report.loopComplete
                      ? 'Loop complete: every link verified — package hash, consumed approval, and receipts on disk.'
                      : `Loop not fully verified — ${report.gaps.length} gap(s) named in the report.`}
                  </p>
                  {report.gaps.map((gap) => (
                    <div className="fl-qbox" key={gap}>
                      <span>{gap}</span>
                    </div>
                  ))}
                  {report.apply ? (
                    <>
                      <p className="fl-plabel" style={{ marginTop: 14 }}>
                        Apply chain — stage by stage
                      </p>
                      <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.done.applyOutcome">
                        {report.apply.applied
                          ? `Applied — copy-only source mutation completed into ${report.apply.workspacePath || 'the governed workspace'}.`
                          : `Not applied${report.apply.refusedReason ? ` — refused: ${report.apply.refusedReason}` : '.'}`}
                      </p>
                      {report.apply.stages.length > 0 ? (
                        <div data-testid="flow.done.applyStages">
                          {report.apply.stages.map((stage) => (
                            <div className="fl-qbox" key={stage.stage} data-testid={`flow.done.applyStage.${stage.stage}`}>
                              <span>
                                <strong style={{ fontSize: 12.5 }}>
                                  {stage.stage} · {stage.succeeded ? 'succeeded' : 'BLOCKED'}
                                </strong>
                                {stage.errors ? (
                                  <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-red, #a63232)' }}>
                                    {stage.errors}
                                  </span>
                                ) : null}
                              </span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <p className="fl-empty">No apply stages recorded — the chain never started.</p>
                      )}
                      <p className="fl-plabel" style={{ marginTop: 14 }}>
                        Receipts — the evidence chain
                      </p>
                      <div data-testid="flow.done.receipts">
                        {report.apply.receipts.map((receipt) => (
                          <div className="fl-qbox" key={receipt.name}>
                            <span>
                              {receipt.name} · {receipt.existsOnDisk ? 'on disk' : 'MISSING'}
                              {receipt.path ? (
                                <span style={{ display: 'block', fontSize: 11.5, color: 'var(--fl-muted)' }}>{receipt.path}</span>
                              ) : null}
                            </span>
                          </div>
                        ))}
                      </div>
                    </>
                  ) : null}
                  <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
                    Copy-only: commit, push, and release remain separate governed steps this loop does not have.
                  </p>
                  <button className="fl-btn" onClick={onOpenGovernanceLibrary} data-testid="flow.done.openGovernance">
                    Open Governance Library
                  </button>
                </>
              )}
            </>
          ) : (
            <>
          <p className="fl-plabel">{ticket === null ? 'Shaping discussion' : 'Ticket detail'}</p>

          {ticket === null ? (
            <>
              {discussion.length === 0 ? (
                <p className="fl-empty">Describe the requirement. The discussion produces criteria — nothing here is authority.</p>
              ) : (
                discussion.map((entry) => (
                  <div key={entry.id} className={entry.role === 'user' ? 'fl-msg fl-user' : 'fl-msg fl-sys'}>
                    {entry.role === 'assistant' ? <MarkdownRenderer markdown={entry.text} /> : entry.text}
                  </div>
                ))
              )}
              {isThinking ? <div className="fl-msg fl-sys">Thinking…</div> : null}
              <form className="fl-chatform" onSubmit={sendPrompt}>
                <input
                  value={prompt}
                  onChange={(event) => setPrompt(event.target.value)}
                  placeholder="Describe or refine the requirement"
                  data-testid="flow.shape.prompt"
                />
                <button className="fl-btn" type="submit" disabled={isThinking}>
                  Send
                </button>
              </form>
              <form
                className="fl-chatform"
                onSubmit={(event) => {
                  event.preventDefault();
                  addCriterion(criterionInput);
                  setCriterionInput('');
                }}
              >
                <input
                  value={criterionInput}
                  onChange={(event) => setCriterionInput(event.target.value)}
                  placeholder="Add an acceptance criterion"
                  data-testid="flow.shape.addCriterion"
                />
                <button className="fl-btn" type="submit">
                  Add criterion
                </button>
              </form>
            </>
          ) : (
            <>
              <p style={{ fontSize: 13.5, color: 'var(--fl-ink2)', marginTop: 0 }}>{draft.summary || 'No summary recorded.'}</p>
              <p className="fl-plabel" style={{ marginTop: 14 }}>
                Build readiness
              </p>
              {readiness === null ? (
                <p className="fl-empty" data-testid="flow.ticket.readiness">
                  Readiness not loaded yet. Next safe action: wait for the backend readiness check or refresh the ticket.
                </p>
              ) : readiness.isReady ? (
                <p style={{ fontSize: 13.5, color: 'var(--fl-acc-ink)' }} data-testid="flow.ticket.readiness">
                  Ready to build. {readiness.message ?? ''}
                </p>
              ) : (
                <>
                  <p style={{ fontSize: 13.5, color: 'var(--fl-gate-ink)' }} data-testid="flow.ticket.readiness">
                    {readiness.message ?? 'Blocked.'} Next safe action: resolve the backend readiness blockers below.
                  </p>
                  {(readiness.blockingIssues ?? []).map((issue) => (
                    <div className="fl-qbox" key={issue}>
                      <span>{issue}</span>
                    </div>
                  ))}
                </>
              )}
              <p className="fl-plabel" style={{ marginTop: 14 }}>
                Linked run evidence
              </p>
              {evidenceLoadState === 'loading' ? (
                <p className="fl-empty" data-testid="flow.ticket.linkedRun">
                  Loading linked run evidence from the backend...
                </p>
              ) : evidenceLoadState === 'error' ? (
                <p className="fl-empty" data-testid="flow.ticket.linkedRun">
                  Linked run evidence unavailable: {evidenceErrorMessage ?? 'unknown error'}. Next safe action: refresh
                  the ticket or open the Governance Library.
                </p>
              ) : evidenceSummary?.latestRun ? (
                <p style={{ fontSize: 13.5, color: 'var(--fl-ink2)' }} data-testid="flow.ticket.linkedRun">
                  Latest run {evidenceSummary.latestRun.runId} · {evidenceSummary.latestRun.status}. The UI hydrates
                  reports from backend evidence; it does not infer apply or approval.
                </p>
              ) : (
                <p className="fl-empty" data-testid="flow.ticket.linkedRun">
                  No linked run evidence yet. Next safe action: start a governed run when readiness is satisfied.
                </p>
              )}
            </>
          )}
            </>
          )}
        </div>

        <div className="fl-workitem-side">
          {workItem ? (
            <section className="fl-panel-box fl-workitem-collaboration" data-testid="flow.workItem.collaboration">
              <div className="fl-workitem-side-heading">
                <p className="fl-plabel">Collaboration</p>
                <button
                  className="fl-btn fl-mini"
                  type="button"
                  onClick={() => onDiscussInChat(workItem.collaboration.linkedChatSessionId)}
                >
                  Discuss in Workshop
                </button>
              </div>
              {memberDirectory ? (
                <button className="fl-btn fl-mini" type="button" onClick={() => setCollaborationEditing((current) => !current)} data-testid="flow.workItem.collaboration.edit">
                  {collaborationEditing ? 'Cancel editing' : 'Edit ownership'}
                </button>
              ) : null}
              {collaborationEditing && memberDirectory ? (
                <div className="fl-workitem-collaboration-editor" data-testid="flow.workItem.collaboration.form">
                  <label>Assignee<select value={assigneeDraft} onChange={(event) => setAssigneeDraft(event.target.value)}><option value="">Not assigned</option>{memberDirectory.members.filter((member) => member.isProjectMember).map((member) => <option key={member.userId} value={member.userId}>{member.displayName}</option>)}</select></label>
                  <label>Waiting on<select value={waitingOnDraft} onChange={(event) => setWaitingOnDraft(event.target.value)}><option value="">No actor</option>{memberDirectory.members.filter((member) => member.isProjectMember).map((member) => <option key={member.userId} value={`user:${member.userId}`}>{member.displayName}</option>)}<option value="role:Project owner">Project owner</option><option value="role:Reviewer">Reviewer</option><option value="role:Approver">Approver</option></select></label>
                  <fieldset><legend>Followers</legend>{memberDirectory.members.filter((member) => member.isProjectMember).map((member) => <label key={member.userId}><input type="checkbox" checked={followerDraft.includes(member.userId)} onChange={(event) => setFollowerDraft((current) => event.target.checked ? [...current, member.userId] : current.filter((id) => id !== member.userId))} />{member.displayName}</label>)}</fieldset>
                  {collaborationError ? <p className="fl-error" role="alert">{collaborationError}</p> : null}
                  {collaborationError?.startsWith('Stale write refused') ? <button className="fl-btn" type="button" onClick={() => void refreshWorkItemProjection(undefined, false)} data-testid="flow.workItem.collaboration.reload">Reload and compare</button> : null}
                  <button className="fl-btn fl-pri" type="button" disabled={collaborationBusy} onClick={() => void saveCollaboration()} data-testid="flow.workItem.collaboration.save">{collaborationBusy ? 'Saving...' : 'Save ownership'}</button>
                </div>
              ) : null}
              <dl className="fl-workitem-facts">
                <div><dt>Assignee</dt><dd>{workItem.collaboration.assignee?.displayName ?? 'Not assigned'}</dd></div>
                <div><dt>Waiting on</dt><dd>{workItem.collaboration.waitingOn?.displayName ?? 'No actor'}</dd></div>
                <div><dt>Followers</dt><dd>{workItem.collaboration.followers?.length ?? 0}</dd></div>
              </dl>
              {(workItem.collaboration.recentActivity?.length ?? 0) > 0 ? (
                <div className="fl-workitem-activity">
                  {workItem.collaboration.recentActivity?.slice(0, 4).map((activity) => (
                    <div key={`${activity.timestampUtc}-${activity.kind}`}>
                      <strong>{activity.summary ?? activity.kind ?? 'Activity'}</strong>
                      <span>{activity.actor?.displayName ?? 'Backend event'}</span>
                    </div>
                  ))}
                </div>
              ) : <p className="fl-empty">No attributed activity was returned.</p>}
            </section>
          ) : null}

          {workItem?.authority ? (
            <section className="fl-panel-box fl-workitem-authority" data-testid="flow.workItem.authority">
              <div className="fl-workitem-side-heading">
                <p className="fl-plabel">Authority</p>
                <span className={workItem.authority.currentUserEligibleToContinue ? 'fl-workitem-authority-badge' : 'fl-workitem-authority-badge fl-muted-badge'}>
                  {workItem.authority.currentUserEligibleToContinue ? 'Eligible' : 'Not eligible'}
                </span>
              </div>
              <dl className="fl-workitem-facts">
                <div><dt>Self approval</dt><dd>{workItem.authority.soloApprovalExceptionAllowed ? 'Solo exception enabled' : 'Different human required'}</dd></div>
                <div><dt>Approved by</dt><dd>{workItem.authority.acceptedApprovalActorDisplayName || (workItem.authority.acceptedApprovalActorId ? `Actor ${workItem.authority.acceptedApprovalActorId}` : 'No accepted approval')}</dd></div>
                <div><dt>Continued by</dt><dd>{workItem.authority.continuationRequestedByUserId ? `User ${workItem.authority.continuationRequestedByUserId}` : 'Not continued'}</dd></div>
              </dl>
              <p>{workItem.authority.selfApprovalPolicy}</p>
              {(workItem.authority.eligibleApprovers?.length ?? 0) > 0 ? (
                <div className="fl-workitem-authority-list">
                  {workItem.authority.eligibleApprovers?.map((actor) => (
                    <span className="fl-chip" key={actor.userId}>
                      {actor.displayName} - {actor.projectRole || 'Project member'}
                    </span>
                  ))}
                </div>
              ) : (
                <p className="fl-empty">No eligible reviewer was returned.</p>
              )}
              {workItem.authority.soloApprovalExceptionUsed ? (
                <p className="fl-workitem-authority-warning">Solo approval exception used and recorded.</p>
              ) : null}
              <small>{workItem.authority.boundary}</small>
            </section>
          ) : null}

          <ContractRail
            criteria={draft.criteria}
            openQuestions={draft.openQuestions}
            architectureRefs={draft.architectureRefs}
            summary={workItem ? {
              criterionCount: workItem.contract.acceptanceCriterionCount ?? 0,
              affectedFileCount: workItem.contract.affectedFileCount ?? 0
            } : undefined}
          onConfirmCriterion={
            ticket === null
              ? (id) =>
                  setDraft((prev) => ({
                    ...prev,
                    criteria: prev.criteria.map((c) => (c.id === id ? { ...c, confirmed: true } : c))
                  }))
              : undefined
          }
          onResolveQuestion={
            ticket === null
              ? (id) =>
                  setDraft((prev) => ({
                    ...prev,
                    openQuestions: prev.openQuestions.map((q) => (q.id === id ? { ...q, resolved: true } : q))
                  }))
              : undefined
          }
          />
        </div>
      </div>

      <div className="fl-foot">
        {ticket === null ? (
          <>
            <span className={shapeBlockers.length === 0 ? 'fl-gatemsg fl-okmsg' : 'fl-gatemsg'} data-testid="flow.shape.gate">
              {shapeBlockers.length === 0
                ? 'Readiness gate: satisfied. Promotion creates the ticket — it does not approve anything downstream.'
                : `Readiness gate: blocked — ${shapeBlockers.join(', ')}. Next safe action: ${
                    unresolvedQuestions.length > 0 ? 'resolve the open question.' : unconfirmedCriteria.length > 0 ? 'confirm or remove the pending criterion.' : 'add acceptance criteria.'
                  }`}
            </span>
            <button
              className="fl-btn fl-pri"
              disabled={shapeBlockers.length > 0 || isPromoting}
              onClick={() => void promoteToTicket()}
              data-testid="flow.shape.promote"
            >
              {isPromoting ? 'Promoting…' : 'Promote to ticket'}
            </button>
          </>
        ) : workItem && gateNotice ? (
          <span
            className={workItem.gate.state?.toLowerCase() === 'blocked' ? 'fl-gatemsg' : 'fl-gatemsg fl-okmsg'}
            data-testid={`flow.${stage}.gate`}
          >
            {gateNotice}
          </span>
        ) : null}
      </div>
    </div>
  );
}
