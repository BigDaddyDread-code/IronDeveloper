import { useCallback, useEffect, useMemo, useState } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  WorkflowReadOnlyApiEnvelope,
  WorkflowReadOnlyIssue,
  WorkflowRunDetailData,
  WorkflowRunSummaryData,
  WorkflowStepDetailData,
  WorkflowStepSummaryData
} from '../../api/types';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { useSessionContext } from '../../state/useSessionContext';
import type {
  WorkflowReferenceView,
  WorkflowRunStepViewerDetail,
  WorkflowRunStepViewerFilters,
  WorkflowRunStepViewerLoadStatus,
  WorkflowRunViewerItem,
  WorkflowStepViewerItem
} from './WorkflowRunStepViewerTypes';
import {
  referencesFromEvidence,
  referencesFromGrounding,
  runItemFromDetail,
  runItemFromSummary,
  stepItemFromDetail,
  stepItemFromSummary
} from './WorkflowRunStepViewerTypes';

interface WorkflowRunStepViewerRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const emptyFilters: WorkflowRunStepViewerFilters = {
  projectReferenceId: '',
  workflowRunId: '',
  workflowStepId: '',
  correlationId: '',
  workflowStatus: '',
  stepStatus: '',
  workflowKind: '',
  fromUtc: '',
  toUtc: '',
  take: '50'
};

const boundaryWarnings = [
  'Read-only view',
  'Workflow visibility is not workflow authority',
  'Workflow status is not transition permission',
  'Step status is not execution permission',
  'Refresh is not retry'
];

const redactedWorkflowText = '[redacted workflow viewer text]';

const unsafeWorkflowTextMarkers = [
  'payload' + 'json',
  'workflow' + 'payloadjson',
  'workflow payload json',
  'step' + 'payloadjson',
  'step payload json',
  'raw' + 'workflowpayload',
  'raw workflow payload',
  'raw' + 'steppayload',
  'raw step payload',
  'raw' + 'prompt',
  'raw prompt',
  'raw' + 'completion',
  'raw completion',
  'raw' + 'tooloutput',
  'raw tool output',
  'raw' + 'commandoutput',
  'raw command output',
  'entire' + 'patch',
  'entire patch',
  'private' + 'reasoning',
  'private reasoning',
  'hidden' + 'reasoning',
  'hidden reasoning',
  'chain' + 'ofthought',
  'chain of thought',
  'chain-of-thought',
  'scratchpad',
  'approval granted',
  'execution allowed',
  'transition allowed',
  'tool executed',
  'source mutated',
  'memory promoted',
  'release approved'
];

export function WorkflowRunStepViewerRoute({ onRouteReady }: WorkflowRunStepViewerRouteProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<WorkflowRunStepViewerFilters>(emptyFilters);
  const [status, setStatus] = useState<WorkflowRunStepViewerLoadStatus>('idle');
  const [message, setMessage] = useState('Set a project reference, then search workflow run and step evidence.');
  const [runs, setRuns] = useState<WorkflowRunViewerItem[]>([]);
  const [steps, setSteps] = useState<WorkflowStepViewerItem[]>([]);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedStepId, setSelectedStepId] = useState<string | null>(null);
  const [detail, setDetail] = useState<WorkflowRunStepViewerDetail | null>(null);
  const [issues, setIssues] = useState<WorkflowReadOnlyIssue[]>([]);
  const [lastBoundary, setLastBoundary] = useState<string[]>(boundaryWarnings);

  const canQuery = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const safeText = useCallback((value: unknown, fallback = '') => {
    if (value === undefined || value === null) {
      return fallback;
    }

    const text = String(value);
    const normalized = text.toLowerCase().replace(/[\s_-]+/g, ' ').trim();
    const compact = normalized.replace(/\s+/g, '');
    const unsafe = unsafeWorkflowTextMarkers.some((marker) => {
      const markerNormalized = marker.toLowerCase().replace(/[\s_-]+/g, ' ').trim();
      const markerCompact = markerNormalized.replace(/\s+/g, '');
      return normalized.includes(markerNormalized) || compact.includes(markerCompact);
    });

    return unsafe ? redactedWorkflowText : text;
  }, []);

  const updateFilter = useCallback(
    (key: keyof WorkflowRunStepViewerFilters, value: string) => {
      setFilters((current) => ({ ...current, [key]: safeText(value) }));
    },
    [safeText]
  );

  const resetFilters = useCallback(() => {
    setFilters(emptyFilters);
    setRuns([]);
    setSteps([]);
    setSelectedRunId(null);
    setSelectedStepId(null);
    setDetail(null);
    setIssues([]);
    setLastBoundary(boundaryWarnings);
    setStatus('idle');
    setMessage('Filters cleared. This did not retry, resume, or move any workflow.');
  }, []);

  const captureBoundary = useCallback((response: WorkflowReadOnlyApiEnvelope<unknown>) => {
    const warnings = response.warnings?.map((warning) => safeText(warning)).filter(Boolean) ?? [];
    setLastBoundary([...boundaryWarnings, ...warnings]);
  }, [safeText]);

  const search = useCallback(async () => {
    if (!canQuery) {
      setStatus('error');
      setMessage('Workflow viewer requires API connection and authentication.');
      return;
    }

    const projectId = filters.projectReferenceId.trim();
    if (!projectId) {
      setStatus('error');
      setMessage('Project reference is required before reading workflow evidence.');
      return;
    }

    const controller = new AbortController();
    setStatus('loading');
    setMessage('Reading workflow runs and steps through GET-only endpoints.');

    try {
      const nextRuns = await loadRuns(projectId, filters, controller.signal);
      const visibleRuns = filterRuns(nextRuns.runs, filters);
      const nextIssues = collectIssues(nextRuns.rawIssues);
      setRuns(visibleRuns);

      const runForStep = filters.workflowRunId.trim() || visibleRuns[0]?.workflowRunId || '';
      if (runForStep) {
        const nextSteps = await loadSteps(projectId, runForStep, filters, controller.signal);
        setSteps(filterSteps(nextSteps.steps, filters));
        nextIssues.push(...collectIssues(nextSteps.rawIssues));
      } else {
        setSteps([]);
      }

      setIssues(nextIssues);
      setSelectedRunId(visibleRuns[0]?.workflowRunId ?? null);
      setSelectedStepId(null);
      setDetail(null);
      setStatus(visibleRuns.length > 0 ? 'ready' : 'empty');
      setMessage(
        visibleRuns.length > 0
          ? 'Workflow evidence loaded. Status is visible only; it is not transition permission.'
          : 'No workflow runs matched the current read-only filters.'
      );
    } catch (error) {
      setStatus('error');
      setMessage(error instanceof IronDevApiError ? error.message : 'Workflow read failed without changing workflow state.');
      setRuns([]);
      setSteps([]);
      setDetail(null);
    }

    async function loadRuns(projectId: string, current: WorkflowRunStepViewerFilters, signal: AbortSignal) {
      const take = boundedTake(current.take);
      if (current.workflowRunId.trim()) {
        const response = await session.client.getWorkflowRun(current.workflowRunId.trim(), projectId, signal);
        captureBoundary(response);
        const run = response.data ? runItemFromDetail(sanitizeRunDetail(response.data), safeText) : null;
        return {
          runs: run ? [run] : [],
          rawIssues: [...(response.errors ?? [])]
        };
      }

      if (current.correlationId.trim()) {
        const response = await session.client.listWorkflowRunsByCorrelation(current.correlationId.trim(), projectId, take, signal);
        captureBoundary(response);
        return {
          runs: sanitizeRunSummaries(response.data?.runs ?? []).map((run) => runItemFromSummary(run, safeText)),
          rawIssues: [...(response.errors ?? []), ...(response.data?.issues ?? [])]
        };
      }

      const response = await session.client.listWorkflowRuns(projectId, take, signal);
      captureBoundary(response);
      return {
        runs: sanitizeRunSummaries(response.data?.runs ?? []).map((run) => runItemFromSummary(run, safeText)),
        rawIssues: [...(response.errors ?? []), ...(response.data?.issues ?? [])]
      };
    }

    async function loadSteps(projectId: string, workflowRunId: string, current: WorkflowRunStepViewerFilters, signal: AbortSignal) {
      const take = boundedTake(current.take);
      if (current.workflowStepId.trim()) {
        const response = await session.client.getWorkflowStep(workflowRunId, current.workflowStepId.trim(), projectId, signal);
        captureBoundary(response);
        const step = response.data ? stepItemFromDetail(sanitizeStepDetail(response.data), safeText) : null;
        return {
          steps: step ? [step] : [],
          rawIssues: [...(response.errors ?? [])]
        };
      }

      const response = await session.client.listWorkflowSteps(workflowRunId, projectId, take, signal);
      captureBoundary(response);
      return {
        steps: sanitizeStepSummaries(response.data?.steps ?? []).map((step) => stepItemFromSummary(step, safeText)),
        rawIssues: [...(response.errors ?? []), ...(response.data?.issues ?? [])]
      };
    }
  }, [canQuery, captureBoundary, filters, safeText, session.client]);

  const openRun = useCallback(
    async (run: WorkflowRunViewerItem) => {
      if (!canQuery) {
        setMessage('API connection is required before opening workflow run details.');
        return;
      }

      setSelectedRunId(run.workflowRunId);
      setSelectedStepId(null);
      setStatus('loading');

      try {
        const response = await session.client.getWorkflowRun(run.workflowRunId, run.projectReferenceId, undefined);
        captureBoundary(response);
        const runDetail = response.data ? sanitizeRunDetail(response.data) : null;
        const nextSteps = runDetail?.steps ?? [];
        setSteps(nextSteps.map((step) => stepItemFromSummary(step, safeText)));
        setDetail(
          runDetail
            ? {
                run: runItemFromDetail(runDetail, safeText),
                step: null,
                evidenceReferences: referencesFromEvidence(runDetail.evidenceReferences, safeText),
                groundingReferences: referencesFromGrounding(runDetail.groundingReferences, safeText),
                warnings: response.warnings?.map((warning) => safeText(warning)).filter(Boolean) ?? []
              }
            : null
        );
        setIssues(response.errors ?? []);
        setStatus(runDetail ? 'ready' : 'empty');
        setMessage(runDetail ? 'Workflow run detail opened for inspection only.' : 'Workflow run detail was not returned.');
      } catch (error) {
        setStatus('error');
        setMessage(error instanceof IronDevApiError ? error.message : 'Workflow run detail read failed.');
      }
    },
    [canQuery, captureBoundary, safeText, session.client]
  );

  const openStep = useCallback(
    async (step: WorkflowStepViewerItem) => {
      if (!canQuery) {
        setMessage('API connection is required before opening workflow step details.');
        return;
      }

      setSelectedRunId(step.workflowRunId);
      setSelectedStepId(step.workflowRunStepId);
      setStatus('loading');

      try {
        const response = await session.client.getWorkflowStep(
          step.workflowRunId,
          step.workflowRunStepId,
          step.projectReferenceId,
          undefined
        );
        captureBoundary(response);
        const stepDetail = response.data ? sanitizeStepDetail(response.data) : null;
        setDetail(
          stepDetail
            ? {
                run: runs.find((run) => run.workflowRunId === step.workflowRunId) ?? null,
                step: stepItemFromDetail(stepDetail, safeText),
                evidenceReferences: referencesFromEvidence(stepDetail.evidenceReferences, safeText),
                groundingReferences: referencesFromGrounding(stepDetail.groundingReferences, safeText),
                warnings: response.warnings?.map((warning) => safeText(warning)).filter(Boolean) ?? []
              }
            : null
        );
        setIssues(response.errors ?? []);
        setStatus(stepDetail ? 'ready' : 'empty');
        setMessage(stepDetail ? 'Workflow step detail opened for inspection only.' : 'Workflow step detail was not returned.');
      } catch (error) {
        setStatus('error');
        setMessage(error instanceof IronDevApiError ? error.message : 'Workflow step detail read failed.');
      }
    },
    [canQuery, captureBoundary, runs, safeText, session.client]
  );

  const copyValue = useCallback(
    async (label: string, value: string) => {
      const safeValue = safeText(value);
      if (!safeValue || safeValue === redactedWorkflowText) {
        setMessage(`${label} was not copied because it was unavailable or unsafe.`);
        return;
      }

      await navigator.clipboard?.writeText(safeValue);
      setMessage(`${label} copied. Copying an ID does not grant workflow authority.`);
    },
    [safeText]
  );

  const openRelated = useCallback((label: string, target: string) => {
    setMessage(`${label} read-only path: ${target}. Opening related evidence does not move workflow state.`);
  }, []);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: 'workflow-runs.search',
        label: 'Search',
        intent: 'primary',
        onExecute: search,
        disabled: status === 'loading',
        testId: 'workflow-runs.command.search'
      },
      {
        id: 'workflow-runs.refresh',
        label: 'Refresh',
        intent: 'secondary',
        onExecute: search,
        disabled: status === 'loading',
        testId: 'workflow-runs.command.refresh'
      },
      {
        id: 'workflow-runs.clear',
        label: 'Clear Filters',
        intent: 'ghost',
        onExecute: resetFilters,
        disabled: status === 'loading',
        testId: 'workflow-runs.command.clear'
      }
    ],
    [resetFilters, search, status]
  );

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: canQuery ? null : 'Workflow run viewer requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: `${runs.length} run(s)`, testId: 'workflow-runs.chip.runs' },
        { label: `${steps.length} step(s)`, testId: 'workflow-runs.chip.steps' },
        { label: 'Read-only', testId: 'workflow-runs.chip.readonly' }
      ]
    }),
    [canQuery, commands, runs.length, steps.length]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  return (
    <main className="workflow-run-step-workspace" data-testid="workflow-runs.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div>
            <p className="eyebrow">Workflow evidence viewer</p>
            <h1>Workflow Run/Step Viewer</h1>
            <p className="lede">
              Inspect workflow runs, workflow steps, and safe references through the existing read-only workflow API.
            </p>
          </div>
          <StatusBadge status={status === 'error' ? 'danger' : status === 'loading' ? 'warning' : 'ready'}>
            {status === 'loading' ? 'Reading' : 'Read-only'}
          </StatusBadge>
        </div>

        <div className="workflow-run-step-banner" data-testid="workflow-runs.boundary-banner">
          {boundaryWarnings.map((warning) => (
            <span key={warning}>{warning}</span>
          ))}
        </div>

        <Surface className="workflow-run-step-filters" data-testid="workflow-runs.filters">
          <label>
            Project reference
            <input
              value={filters.projectReferenceId}
              onChange={(event) => updateFilter('projectReferenceId', event.currentTarget.value)}
              placeholder="Project GUID"
              data-testid="workflow-runs.filter.project"
            />
          </label>
          <label>
            Workflow run ID
            <input
              value={filters.workflowRunId}
              onChange={(event) => updateFilter('workflowRunId', event.currentTarget.value)}
              placeholder="Workflow run GUID"
              data-testid="workflow-runs.filter.run"
            />
          </label>
          <label>
            Workflow step ID
            <input
              value={filters.workflowStepId}
              onChange={(event) => updateFilter('workflowStepId', event.currentTarget.value)}
              placeholder="Workflow step GUID"
              data-testid="workflow-runs.filter.step"
            />
          </label>
          <label>
            Correlation ID
            <input
              value={filters.correlationId}
              onChange={(event) => updateFilter('correlationId', event.currentTarget.value)}
              placeholder="Correlation GUID"
              data-testid="workflow-runs.filter.correlation"
            />
          </label>
          <label>
            Workflow status
            <input
              value={filters.workflowStatus}
              onChange={(event) => updateFilter('workflowStatus', event.currentTarget.value)}
              placeholder="Recorded status"
              data-testid="workflow-runs.filter.workflow-status"
            />
          </label>
          <label>
            Step status
            <input
              value={filters.stepStatus}
              onChange={(event) => updateFilter('stepStatus', event.currentTarget.value)}
              placeholder="Recorded step status"
              data-testid="workflow-runs.filter.step-status"
            />
          </label>
          <label>
            Workflow kind
            <input
              value={filters.workflowKind}
              onChange={(event) => updateFilter('workflowKind', event.currentTarget.value)}
              placeholder="Type/name contains"
              data-testid="workflow-runs.filter.kind"
            />
          </label>
          <label>
            From UTC
            <input
              value={filters.fromUtc}
              onChange={(event) => updateFilter('fromUtc', event.currentTarget.value)}
              placeholder="2026-01-01T00:00:00Z"
              data-testid="workflow-runs.filter.from"
            />
          </label>
          <label>
            To UTC
            <input
              value={filters.toUtc}
              onChange={(event) => updateFilter('toUtc', event.currentTarget.value)}
              placeholder="2026-01-02T00:00:00Z"
              data-testid="workflow-runs.filter.to"
            />
          </label>
          <label>
            Take
            <input
              value={filters.take}
              onChange={(event) => updateFilter('take', event.currentTarget.value)}
              placeholder="50"
              data-testid="workflow-runs.filter.take"
            />
          </label>
          <div className="workflow-run-step-filter-actions">
            <button type="button" onClick={search} disabled={status === 'loading'} data-testid="workflow-runs.search">
              Search
            </button>
            <button type="button" onClick={search} disabled={status === 'loading'} data-testid="workflow-runs.refresh">
              Refresh
            </button>
            <button type="button" onClick={resetFilters} disabled={status === 'loading'} data-testid="workflow-runs.clear">
              Clear Filters
            </button>
          </div>
        </Surface>

        <p className="workflow-run-step-message" data-testid="workflow-runs.message">
          {message}
        </p>

        <div className="workflow-run-step-grid">
          <Surface className="workflow-run-step-panel">
            <div className="workflow-run-step-panel__header">
              <h2>Workflow runs</h2>
              <span>{runs.length} returned</span>
            </div>
            {runs.length === 0 ? (
              <EmptyState title="No workflow runs loaded" body="Search existing workflow evidence to inspect run state." />
            ) : (
              <div className="workflow-run-step-list" data-testid="workflow-runs.list">
                {runs.map((run) => (
                  <article
                    key={run.workflowRunId}
                    className={run.workflowRunId === selectedRunId ? 'workflow-run-step-card is-selected' : 'workflow-run-step-card'}
                    data-testid="workflow-runs.run-card"
                  >
                    <div>
                      <h3>{run.workflowName}</h3>
                      <p>{run.workflowType}</p>
                    </div>
                    <StatusBadge status={run.hasAuthorityFlag ? 'danger' : 'neutral'}>{run.status}</StatusBadge>
                    <dl>
                      <dt>Workflow ID</dt>
                      <dd>{run.workflowRunId}</dd>
                      <dt>Correlation</dt>
                      <dd>{run.correlationId}</dd>
                      <dt>Steps</dt>
                      <dd>{run.stepCount}</dd>
                      <dt>Created</dt>
                      <dd>{displayDate(run.createdUtc)}</dd>
                    </dl>
                    <div className="workflow-run-step-card__actions">
                      <button type="button" onClick={() => openRun(run)} data-testid="workflow-runs.open-run">
                        Open Run
                      </button>
                      <button type="button" onClick={() => copyValue('Workflow ID', run.workflowRunId)}>
                        Copy Workflow ID
                      </button>
                      <button type="button" onClick={() => copyValue('Correlation ID', run.correlationId)}>
                        Copy Correlation ID
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </Surface>

          <Surface className="workflow-run-step-panel">
            <div className="workflow-run-step-panel__header">
              <h2>Workflow steps</h2>
              <span>{steps.length} returned</span>
            </div>
            {steps.length === 0 ? (
              <EmptyState title="No workflow steps loaded" body="Open a run or search a run ID to inspect recorded steps." />
            ) : (
              <div className="workflow-run-step-list" data-testid="workflow-runs.steps">
                {steps.map((step) => (
                  <article
                    key={step.workflowRunStepId}
                    className={step.workflowRunStepId === selectedStepId ? 'workflow-run-step-card is-selected' : 'workflow-run-step-card'}
                    data-testid="workflow-runs.step-card"
                  >
                    <div>
                      <h3>{step.stepName}</h3>
                      <p>
                        #{step.sequenceNumber} · {step.stepType}
                      </p>
                    </div>
                    <StatusBadge status={step.hasAuthorityFlag ? 'danger' : 'neutral'}>{step.status}</StatusBadge>
                    <dl>
                      <dt>Step ID</dt>
                      <dd>{step.workflowRunStepId}</dd>
                      <dt>Step key</dt>
                      <dd>{step.stepKey}</dd>
                      <dt>Agent</dt>
                      <dd>{step.agentRole}</dd>
                      <dt>Created</dt>
                      <dd>{displayDate(step.createdUtc)}</dd>
                    </dl>
                    <div className="workflow-run-step-card__actions">
                      <button type="button" onClick={() => openStep(step)} data-testid="workflow-runs.open-step">
                        Open Step
                      </button>
                      <button type="button" onClick={() => copyValue('Step ID', step.workflowRunStepId)}>
                        Copy Step ID
                      </button>
                      <button type="button" onClick={() => copyValue('Correlation ID', step.correlationId)}>
                        Copy Correlation ID
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </Surface>
        </div>

        <Surface className="workflow-run-step-detail" data-testid="workflow-runs.detail">
          <div className="workflow-run-step-panel__header">
            <h2>Selected evidence</h2>
            <span>Safe summaries only</span>
          </div>
          {detail ? (
            <div className="workflow-run-step-detail-grid">
              <DetailBlock title="Run" item={detail.run} />
              <DetailBlock title="Step" item={detail.step} />
              <ReferenceList title="Evidence references" references={detail.evidenceReferences} testId="workflow-runs.evidence" />
              <ReferenceList title="Grounding references" references={detail.groundingReferences} testId="workflow-runs.grounding" />
              <div>
                <h3>Related read-only paths</h3>
                <div className="workflow-run-step-related">
                  <button type="button" onClick={() => openRelated('Open Trace', tracePath(detail))}>
                    Open Trace
                  </button>
                  <button type="button" onClick={() => openRelated('Open Timeline', timelinePath(detail))}>
                    Open Timeline
                  </button>
                  <button type="button" onClick={() => openRelated('Open Diagnosis', diagnosisPath(detail))}>
                    Open Diagnosis
                  </button>
                  <button type="button" onClick={() => openRelated('Open Agent Health', agentHealthPath(detail))}>
                    Open Agent Health
                  </button>
                  <button type="button" onClick={() => openRelated('Open Tool Gate Ledger', toolGatePath(detail))}>
                    Open Tool Gate Ledger
                  </button>
                  <button type="button" onClick={() => openRelated('Open Dogfood Receipts', dogfoodPath(detail))}>
                    Open Dogfood Receipts
                  </button>
                  <button type="button" onClick={() => openRelated('Open Approval Packages', approvalPath(detail))}>
                    Open Approval Packages
                  </button>
                </div>
              </div>
              <ReferenceList
                title="Boundary warnings"
                references={lastBoundary.map((warning, index) => ({
                  id: `boundary-${index + 1}`,
                  type: 'boundary',
                  label: warning,
                  summary: warning,
                  authority: false
                }))}
                testId="workflow-runs.boundary"
              />
            </div>
          ) : (
            <EmptyState title="No detail opened" body="Open a run or step to inspect safe evidence references." />
          )}
        </Surface>

        {issues.length > 0 ? (
          <Surface className="workflow-run-step-issues">
            <div data-testid="workflow-runs.issues">
            <h2>Read issues</h2>
            <ul>
              {issues.map((issue, index) => (
                <li key={`${issue.code}-${index}`}>
                  <strong>{safeText(issue.code, 'issue')}</strong>: {safeText(issue.message, 'message unavailable')}
                </li>
              ))}
            </ul>
            </div>
          </Surface>
        ) : null}

        <footer className="workflow-run-step-footer" data-testid="workflow-runs.footer">
          This UI cannot start, continue, transition, retry, repair, execute workflow, invoke tools, dispatch agents, apply
          source, or release software.
        </footer>
      </section>
    </main>
  );

  function sanitizeRunSummaries(values: WorkflowRunSummaryData[]) {
    return values.map((run) => ({
      ...run,
      workflowRunId: safeText(run.workflowRunId),
      projectId: safeText(run.projectId),
      workflowType: safeText(run.workflowType),
      workflowName: safeText(run.workflowName),
      status: safeText(run.status),
      subjectType: safeText(run.subjectType),
      subjectId: safeText(run.subjectId),
      correlationId: safeText(run.correlationId),
      causationId: safeText(run.causationId),
      createdUtc: safeText(run.createdUtc)
    }));
  }

  function sanitizeRunDetail(run: WorkflowRunDetailData): WorkflowRunDetailData {
    return {
      ...sanitizeRunSummaries([run])[0],
      subjectSummary: safeText(run.subjectSummary),
      steps: sanitizeStepSummaries(run.steps ?? []),
      evidenceReferences: run.evidenceReferences?.map((reference) => ({
        ...reference,
        evidenceReferenceId: safeText(reference.evidenceReferenceId),
        evidenceId: safeText(reference.evidenceId),
        evidenceType: safeText(reference.evidenceType),
        evidenceLabel: safeText(reference.evidenceLabel),
        safeSummary: safeText(reference.safeSummary)
      })),
      groundingReferences: run.groundingReferences?.map((reference) => ({
        ...reference,
        groundingReferenceId: safeText(reference.groundingReferenceId),
        groundingId: safeText(reference.groundingId),
        groundingType: safeText(reference.groundingType),
        claim: safeText(reference.claim),
        safeSummary: safeText(reference.safeSummary)
      }))
    };
  }

  function sanitizeStepSummaries(values: WorkflowStepSummaryData[]) {
    return values.map((step) => ({
      ...step,
      workflowRunStepId: safeText(step.workflowRunStepId),
      workflowRunId: safeText(step.workflowRunId),
      projectId: safeText(step.projectId),
      stepKey: safeText(step.stepKey),
      stepName: safeText(step.stepName),
      stepType: safeText(step.stepType),
      status: safeText(step.status),
      agentRole: safeText(step.agentRole),
      agentId: safeText(step.agentId),
      subjectType: safeText(step.subjectType),
      subjectId: safeText(step.subjectId),
      correlationId: safeText(step.correlationId),
      causationId: safeText(step.causationId),
      createdUtc: safeText(step.createdUtc)
    }));
  }

  function sanitizeStepDetail(step: WorkflowStepDetailData): WorkflowStepDetailData {
    return {
      ...sanitizeStepSummaries([step])[0],
      safeSummary: safeText(step.safeSummary),
      evidenceReferences: step.evidenceReferences?.map((reference) => ({
        ...reference,
        evidenceReferenceId: safeText(reference.evidenceReferenceId),
        evidenceId: safeText(reference.evidenceId),
        evidenceType: safeText(reference.evidenceType),
        evidenceLabel: safeText(reference.evidenceLabel),
        safeSummary: safeText(reference.safeSummary)
      })),
      groundingReferences: step.groundingReferences?.map((reference) => ({
        ...reference,
        groundingReferenceId: safeText(reference.groundingReferenceId),
        groundingId: safeText(reference.groundingId),
        groundingType: safeText(reference.groundingType),
        claim: safeText(reference.claim),
        safeSummary: safeText(reference.safeSummary)
      }))
    };
  }
}

function filterRuns(items: WorkflowRunViewerItem[], filters: WorkflowRunStepViewerFilters) {
  return items.filter((item) => {
    return (
      textContains(item.status, filters.workflowStatus) &&
      (textContains(item.workflowType, filters.workflowKind) || textContains(item.workflowName, filters.workflowKind)) &&
      dateWithin(item.createdUtc, filters.fromUtc, filters.toUtc)
    );
  });
}

function filterSteps(items: WorkflowStepViewerItem[], filters: WorkflowRunStepViewerFilters) {
  return items.filter((item) => textContains(item.status, filters.stepStatus) && dateWithin(item.createdUtc, filters.fromUtc, filters.toUtc));
}

function collectIssues(issues: WorkflowReadOnlyIssue[]) {
  return issues.map((issue) => ({
    ...issue,
    code: String(issue.code ?? 'issue'),
    message: String(issue.message ?? 'message unavailable'),
    severity: String(issue.severity ?? 'warning')
  }));
}

function boundedTake(value: string) {
  const numeric = Number.parseInt(value, 10);
  if (!Number.isFinite(numeric)) {
    return 50;
  }

  return Math.min(100, Math.max(1, numeric));
}

function textContains(value: string, filter: string) {
  return !filter.trim() || value.toLowerCase().includes(filter.trim().toLowerCase());
}

function dateWithin(value: string, fromUtc: string, toUtc: string) {
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) {
    return true;
  }

  const from = fromUtc.trim() ? Date.parse(fromUtc.trim()) : Number.NaN;
  const to = toUtc.trim() ? Date.parse(toUtc.trim()) : Number.NaN;

  return (!Number.isFinite(from) || timestamp >= from) && (!Number.isFinite(to) || timestamp <= to);
}

function displayDate(value: string) {
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? new Date(timestamp).toLocaleString() : value;
}

function DetailBlock({ title, item }: { title: string; item: WorkflowRunViewerItem | WorkflowStepViewerItem | null }) {
  if (!item) {
    return (
      <div>
        <h3>{title}</h3>
        <p>No {title.toLowerCase()} detail selected.</p>
      </div>
    );
  }

  return (
    <div data-testid={`workflow-runs.${title.toLowerCase()}-detail`}>
      <h3>{title}</h3>
      <dl>
        {Object.entries(item).map(([key, value]) => (
          <div key={key}>
            <dt>{key}</dt>
            <dd>{String(value)}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

function ReferenceList({ title, references, testId }: { title: string; references: WorkflowReferenceView[]; testId: string }) {
  return (
    <div data-testid={testId}>
      <h3>{title}</h3>
      {references.length === 0 ? (
        <p>No safe references returned.</p>
      ) : (
        <ul>
          {references.map((reference) => (
            <li key={reference.id}>
              <strong>{reference.label}</strong>
              <span>{reference.type}</span>
              <p>{reference.summary}</p>
              {reference.authority ? <em>Authority flag detected and shown as warning only.</em> : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function tracePath(detail: WorkflowRunStepViewerDetail) {
  return `/governance/timeline?workflowRunId=${encodeURIComponent(detail.run?.workflowRunId ?? detail.step?.workflowRunId ?? '')}`;
}

function timelinePath(detail: WorkflowRunStepViewerDetail) {
  return tracePath(detail);
}

function diagnosisPath(detail: WorkflowRunStepViewerDetail) {
  return `/workflow/diagnosis?workflowRunId=${encodeURIComponent(detail.run?.workflowRunId ?? detail.step?.workflowRunId ?? '')}`;
}

function agentHealthPath(detail: WorkflowRunStepViewerDetail) {
  return `/governance/agent-health?workflowRunId=${encodeURIComponent(detail.run?.workflowRunId ?? detail.step?.workflowRunId ?? '')}`;
}

function toolGatePath(detail: WorkflowRunStepViewerDetail) {
  return `/governance/tool-gates?workflowRunId=${encodeURIComponent(detail.run?.workflowRunId ?? detail.step?.workflowRunId ?? '')}`;
}

function dogfoodPath(detail: WorkflowRunStepViewerDetail) {
  return `/governance/dogfood-receipts?workflowRunId=${encodeURIComponent(detail.run?.workflowRunId ?? detail.step?.workflowRunId ?? '')}`;
}

function approvalPath(detail: WorkflowRunStepViewerDetail) {
  return `/governance/approval-packages?workflowRunId=${encodeURIComponent(detail.run?.workflowRunId ?? detail.step?.workflowRunId ?? '')}`;
}
