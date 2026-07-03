import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import type {
  AcceptedApprovalApiError,
  BuildReadinessResult,
  ProjectTicket,
  SkeletonCriticPackage,
  SkeletonRunReport,
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

export function WorkItemScreen({ ticket, onTicketCreated, onBackToBoard }: WorkItemScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();

  const [stage, setStage] = useState<WorkItemStage>(ticket ? 'ticket' : 'shape');
  const [draft, setDraft] = useState<ShapeDraft>(ticket ? draftFromTicket(ticket) : emptyShapeDraft());
  const [discussion, setDiscussion] = useState<DiscussionEntry[]>([]);
  const [prompt, setPrompt] = useState('');
  const [isThinking, setIsThinking] = useState(false);
  const [isPromoting, setIsPromoting] = useState(false);
  const [readiness, setReadiness] = useState<BuildReadinessResult | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [run, setRun] = useState<TicketBuildRunDto | null>(null);
  const [report, setReport] = useState<SkeletonRunReport | null>(null);
  const [criticPackage, setCriticPackage] = useState<SkeletonCriticPackage | null>(null);
  const [isStartingRun, setIsStartingRun] = useState(false);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [gateNotice, setGateNotice] = useState<string | null>(null);

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
        { afterStage: 'shape', label: 'readiness', state: shapeBlockers.length === 0 ? 'open' : 'locked' },
        { afterStage: 'ticket', label: 'approval', state: 'locked' }
      ];
    }
    return [
      { afterStage: 'shape', label: 'ready', state: 'open' },
      { afterStage: 'ticket', label: 'readiness', state: readiness?.isReady ? 'open' : 'locked' },
      {
        afterStage: 'review',
        label: 'human gate',
        state: report?.approval?.continuationUnblocked === true ? 'open' : 'locked'
      }
    ];
  }, [stage, shapeBlockers.length, readiness?.isReady, report?.approval?.continuationUnblocked]);

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

  const recordApproval = useCallback(async () => {
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
        evidenceReferences: [`critic-package-sha256:${requirement.targetHash}`],
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
              busyAction={busyAction}
              onRecordApproval={() => void recordApproval()}
              onRequestContinuation={() => void requestContinuation()}
              onRequestApply={() => void requestApply()}
            />
          ) : stage === 'done' ? (
            <>
              <p className="fl-plabel">Loop record</p>
              {report === null ? (
                <p className="fl-empty">Report not loaded.</p>
              ) : (
                <>
                  <p style={{ fontSize: 13.5, marginTop: 0 }} data-testid="flow.done.loop">
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
                        Receipts — the evidence chain
                      </p>
                      {report.apply.receipts.map((receipt) => (
                        <div className="fl-qbox" key={receipt.name}>
                          <span>
                            {receipt.name} · {receipt.existsOnDisk ? 'on disk' : 'MISSING'}
                          </span>
                        </div>
                      ))}
                    </>
                  ) : null}
                  <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
                    Copy-only: commit, push, and release remain separate governed steps this loop does not have.
                  </p>
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
                <p className="fl-empty">Readiness not loaded.</p>
              ) : readiness.isReady ? (
                <p style={{ fontSize: 13.5, color: 'var(--fl-acc-ink)' }}>Ready to build. {readiness.message ?? ''}</p>
              ) : (
                <>
                  <p style={{ fontSize: 13.5, color: 'var(--fl-gate-ink)' }}>{readiness.message ?? 'Blocked.'}</p>
                  {(readiness.blockingIssues ?? []).map((issue) => (
                    <div className="fl-qbox" key={issue}>
                      <span>{issue}</span>
                    </div>
                  ))}
                </>
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
            <span className={readiness?.isReady ? 'fl-gatemsg fl-okmsg' : 'fl-gatemsg'}>
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
