import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import type {
  AcceptedApprovalApiError,
  BuildReadinessResult,
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

interface WorkItemScreenProps {
  ticket: ProjectTicket | null;
  onTicketCreated: (ticket: ProjectTicket) => void;
  onBackToBoard: () => void;
  onOpenGovernanceLibrary: () => void;
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

function stageFromReport(report: SkeletonRunReport): WorkItemStage {
  const status = report.status.toLowerCase();
  if (report.loopComplete || status.includes('applied')) {
    return 'done';
  }
  if (report.approval || report.criticPackage || report.criticReviews.length > 0 || status.includes('approval')) {
    return 'review';
  }
  return 'build';
}

function stageFromLinkedRun(status: string): WorkItemStage {
  if (status === 'needsHumanReview') {
    return 'review';
  }
  if (status === 'passed') {
    return 'build';
  }
  return 'ticket';
}

export function WorkItemScreen({ ticket, onTicketCreated, onBackToBoard, onOpenGovernanceLibrary }: WorkItemScreenProps) {
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
  }, [ticket?.id]);

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
        setStage(stageFromLinkedRun(latestRun.status));

        try {
          const nextReport = await session.client.getSkeletonRunReport(projectId, ticketId, latestRun.runId, controller.signal);
          if (!controller.signal.aborted) {
            setReport(nextReport);
            setRun(runFromReport(nextReport));
            setStage(stageFromReport(nextReport));
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
    report?.approval?.continuationUnblocked
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
        setErrorMessage(error instanceof Error ? error.message : 'Chat failed.');
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

  const startBuildRun = useCallback(async () => {
    if (project.selectedProjectId === null || ticket?.id === undefined || isStartingRun) {
      return;
    }
    setIsStartingRun(true);
    setErrorMessage(null);
    setGateNotice(null);
    try {
      const started = await session.client.startSkeletonRun(project.selectedProjectId, ticket.id);
      setRun(started);
      setStage('build');
      await refreshRunEvidence(started);
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'Could not start the build run.'));
    } finally {
      setIsStartingRun(false);
    }
  }, [project.selectedProjectId, ticket, isStartingRun, session.client, refreshRunEvidence]);

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
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The continuation request failed.'));
    } finally {
      setBusyAction(null);
    }
  }, [project.selectedProjectId, ticket, run, busyAction, session.client, refreshRunEvidence]);

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
      if (result.status === 'Applied') {
        setStage('done');
      }
    } catch (error: unknown) {
      setErrorMessage(describeApiError(error, 'The apply request failed.'));
    } finally {
      setBusyAction(null);
    }
  }, [project.selectedProjectId, ticket, run, busyAction, session.client, refreshRunEvidence]);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12, marginBottom: 10 }}>
        <div>
          <h1 className="fl-h1">{draft.title.trim().length > 0 ? draft.title : 'New work item'}</h1>
          <p className="fl-sub">
            {ticket && ticket.id !== undefined ? `WI-${ticket.id} · ` : ''}
            {stage === 'shape'
              ? 'Shaping — discussion produces the contract.'
              : stage === 'ticket'
                ? 'Ticket — the contract is the gate input.'
                : stage === 'build'
                  ? 'Build — the run reports; it grants nothing.'
                  : stage === 'review'
                    ? 'Review — the critic package and the human gate.'
                    : 'Done — the loop record, verified end to end.'}
          </p>
        </div>
        <button className="fl-btn" onClick={onBackToBoard}>
          Back to board
        </button>
      </div>

      <StageRail activeStage={stage} gates={gates} />

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
          <p className="fl-plabel">{stage === 'shape' ? 'Shaping discussion' : 'Ticket detail'}</p>

          {stage === 'shape' ? (
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

        <ContractRail
          criteria={draft.criteria}
          openQuestions={draft.openQuestions}
          architectureRefs={draft.architectureRefs}
          onConfirmCriterion={
            stage === 'shape'
              ? (id) =>
                  setDraft((prev) => ({
                    ...prev,
                    criteria: prev.criteria.map((c) => (c.id === id ? { ...c, confirmed: true } : c))
                  }))
              : undefined
          }
          onResolveQuestion={
            stage === 'shape'
              ? (id) =>
                  setDraft((prev) => ({
                    ...prev,
                    openQuestions: prev.openQuestions.map((q) => (q.id === id ? { ...q, resolved: true } : q))
                  }))
              : undefined
          }
        />
      </div>

      <div className="fl-foot">
        {stage === 'shape' ? (
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
        ) : stage === 'ticket' ? (
          <>
            <span className={readiness?.isReady ? 'fl-gatemsg fl-okmsg' : 'fl-gatemsg'} data-testid="flow.ticket.gate">
              {readiness?.isReady
                ? 'Readiness gate: satisfied. Starting a run builds and tests in a disposable workspace — it approves nothing.'
                : 'Readiness gate: blocked. The backend explains the block above — the UI never invents one.'}
            </span>
            <button
              className="fl-btn fl-pri"
              disabled={!readiness?.isReady || isStartingRun}
              onClick={() => void startBuildRun()}
              data-testid="flow.ticket.startRun"
            >
              {isStartingRun ? 'Running…' : 'Start build run'}
            </button>
          </>
        ) : stage === 'build' ? (
          <>
            <span
              className={run?.status === 'PausedForApproval' ? 'fl-gatemsg fl-okmsg' : 'fl-gatemsg'}
              data-testid="flow.build.gate"
            >
              {run?.status === 'PausedForApproval'
                ? 'Halted for approval. Halt is not approval — the review stage is where a human decides.'
                : run?.status === 'Failed'
                  ? 'The run blocked. The report above names the reason and the next safe action.'
                  : 'The run reports its own state; the UI never invents one.'}
            </span>
            <button
              className="fl-btn fl-pri"
              disabled={run === null || run.status === 'Failed'}
              onClick={() => setStage('review')}
              data-testid="flow.build.toReview"
            >
              Proceed to review
            </button>
          </>
        ) : stage === 'review' ? (
          <span
            className={report?.approval?.continuationUnblocked ? 'fl-gatemsg fl-okmsg' : 'fl-gatemsg'}
            data-testid="flow.review.gate"
          >
            {gateNotice ??
              (report?.approval?.continuationUnblocked
                ? 'Continuation unblocked by a verified approval. Approval is not apply permission — apply is verified live.'
                : 'Human gate: locked. Only a recorded approval, verified live by the backend, unblocks continuation.')}
          </span>
        ) : (
          <span className="fl-gatemsg fl-okmsg">
            The loop record above is reconstruction from durable evidence — it grants nothing.
          </span>
        )}
      </div>
    </div>
  );
}
