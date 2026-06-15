import { useCallback, useEffect, useMemo, useState } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  GovernanceTraceApiEnvelope,
  GovernanceTraceDetail,
  GovernanceTraceDetailData,
  GovernanceTraceIssue,
  GovernanceTraceListData,
  GovernanceTraceQuery,
  GovernanceTraceSummary,
  GovernanceTraceTimelineItem
} from '../../api/types';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import { useSessionContext } from '../../state/useSessionContext';

interface GovernanceTimelineRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

type TimelineLoadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'validation' | 'error';

interface GovernanceTimelineFilters {
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  causationId: string;
  subjectReferenceId: string;
  eventKind: string;
  sourceComponent: string;
  fromUtc: string;
  toUtc: string;
  take: string;
}

const emptyFilters: GovernanceTimelineFilters = {
  projectReferenceId: '',
  workflowRunId: '',
  workflowStepId: '',
  correlationId: '',
  causationId: '',
  subjectReferenceId: '',
  eventKind: '',
  sourceComponent: '',
  fromUtc: '',
  toUtc: '',
  take: '50'
};

const boundaryWarnings = [
  'Read-only view',
  'Timeline is not authority',
  'Observation is not approval',
  'Traceability is not mutation permission'
];

const redactedTimelineText = '[redacted timeline text]';

const unsafeTraceTextMarkers = [
  'payload' + 'json',
  'raw' + 'payload',
  'raw' + 'prompt',
  'raw prompt',
  'raw' + 'completion',
  'raw completion',
  'raw' + 'tooloutput',
  'raw tool output',
  'raw' + 'commandoutput',
  'raw command output',
  'stdout',
  'stderr',
  'private' + 'reasoning',
  'private reasoning',
  'hidden' + 'reasoning',
  'hidden reasoning',
  'chain' + 'ofthought',
  'chain-of-thought',
  'scratchpad',
  'source' + 'content',
  'source content',
  'source' + 'filecontents',
  'source file contents',
  'patch' + 'payload',
  'patch payload',
  'diff' + 'payload',
  'connection' + 'string',
  'password',
  'secret',
  'api' + 'key',
  'token',
  'credential',
  'bearer '
];

export function GovernanceTimelineRoute({ route, onRouteReady }: GovernanceTimelineRouteProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<GovernanceTimelineFilters>(emptyFilters);
  const [status, setStatus] = useState<TimelineLoadStatus>('idle');
  const [message, setMessage] = useState('Set at least one trace filter, then search the governance timeline.');
  const [traces, setTraces] = useState<GovernanceTraceSummary[]>([]);
  const [selectedTraceId, setSelectedTraceId] = useState<string | null>(null);
  const [detail, setDetail] = useState<GovernanceTraceDetail | null>(null);
  const [detailMessage, setDetailMessage] = useState('Open a trace to inspect safe timeline references.');
  const [issues, setIssues] = useState<GovernanceTraceIssue[]>([]);
  const [relatedMessage, setRelatedMessage] = useState('Related report links stay read-only and do not continue workflow.');
  const [copyMessage, setCopyMessage] = useState('');

  const selectedTrace = useMemo(
    () => traces.find((trace) => safeText(trace.traceId) === selectedTraceId) ?? null,
    [selectedTraceId, traces]
  );

  const canQuery = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const search = useCallback(async () => {
    if (!canQuery) {
      setStatus('error');
      setMessage('Connect IronDev.Api and sign in before loading governance traces.');
      setTraces([]);
      setIssues([]);
      return;
    }

    setStatus('loading');
    setMessage('Loading governance timeline...');
    setIssues([]);

    const controller = new AbortController();

    try {
      const response = await session.client.searchGovernanceTraces(toTraceQuery(filters), controller.signal);
      const responseIssues = [...(response.errors ?? []), ...(response.data?.issues ?? [])];
      const nextTraces = sanitizeTraceSummaries(response.data?.traces ?? []);

      if (responseIssues.length > 0) {
        setIssues(responseIssues);
      }

      setTraces(nextTraces);
      setSelectedTraceId(nextTraces[0]?.traceId ?? null);
      setDetail(null);
      setDetailMessage('Open a trace to inspect safe timeline references.');
      setStatus(nextTraces.length === 0 ? 'empty' : 'loaded');
      setMessage(nextTraces.length === 0 ? 'No governance traces matched those filters.' : `Loaded ${nextTraces.length} governance trace item(s).`);
    } catch (error) {
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setTraces([]);
      setSelectedTraceId(null);
      setDetail(null);
      setStatus(nextIssues.length > 0 ? 'validation' : 'error');
      setMessage(nextIssues.length > 0 ? 'Governance timeline request was rejected by validation.' : 'Governance timeline request failed.');
    } finally {
      controller.abort();
    }
  }, [canQuery, filters, session.client]);

  const openTrace = useCallback(async () => {
    if (!selectedTraceId) {
      setDetail(null);
      setDetailMessage('Select a trace before opening detail.');
      return;
    }

    setDetailMessage('Loading safe trace detail...');
    const controller = new AbortController();

    try {
      const response = await session.client.getGovernanceTrace(selectedTraceId, controller.signal);
      setDetail(sanitizeTraceDetail(response.data?.trace ?? null));
      setDetailMessage(response.data?.trace ? 'Trace detail loaded with safe summaries only.' : 'Trace detail was not returned.');
    } catch (error) {
      setDetail(null);
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setDetailMessage(nextIssues.length > 0 ? 'Trace detail request was rejected by validation.' : 'Trace detail request failed.');
    } finally {
      controller.abort();
    }
  }, [selectedTraceId, session.client]);

  const clearFilters = useCallback(() => {
    setFilters(emptyFilters);
    setIssues([]);
    setMessage('Filters cleared. Search is read-only and does not replay governance.');
  }, []);

  const copyReference = useCallback(() => {
    const reference = selectedTraceId ?? selectedTrace?.correlationId ?? selectedTrace?.workflowRunId ?? '';
    if (!reference) {
      setCopyMessage('Select a trace before copying a reference.');
      return;
    }

    void navigator.clipboard?.writeText(reference).catch(() => undefined);
    setCopyMessage('Reference copied for inspection only. Copy reference is not approval.');
  }, [selectedTrace, selectedTraceId]);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: `workspace.${route.id}.refresh`,
        label: status === 'loading' ? 'Refreshing timeline' : 'Refresh timeline',
        intent: 'secondary',
        onExecute: () => void search(),
        disabled: status === 'loading',
        busy: status === 'loading',
        testId: 'governance-timeline.command.refresh',
        disabledReason: canQuery ? undefined : 'Connect IronDev.Api and sign in before refreshing governance traces.'
      }
    ],
    [canQuery, route.id, search, status]
  );

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: canQuery ? null : 'Governance timeline requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: `${traces.length} trace(s)`, testId: 'governance-timeline.chip.count' },
        { label: 'Read-only', testId: 'governance-timeline.chip.readonly' }
      ],
      blockReasonTestId: canQuery ? undefined : 'governance-timeline.blockedReason'
    }),
    [canQuery, commands, traces.length]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  return (
    <main className="governance-timeline-workspace" data-testid="governance-timeline.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Governance trace explorer</p>
            <h2>Governance Timeline</h2>
            <p>
              Search existing governance traces and inspect safe summaries, correlations, causation references, and related
              operational reports.
            </p>
          </div>
          <div className="governance-timeline-banner" data-testid="governance-timeline.readonlyBanner">
            {boundaryWarnings.map((warning) => (
              <span key={warning}>{warning}</span>
            ))}
          </div>
        </div>

        <Surface testId="governance-timeline.filters" className="governance-timeline-filters">
          <div className="section-heading">
            <p className="eyebrow">Filters</p>
            <h3>Read-only search</h3>
          </div>
          <div className="governance-timeline-filter-grid">
            <FilterInput label="Project reference" name="projectReferenceId" value={filters.projectReferenceId} onChange={setFilters} />
            <FilterInput label="Workflow run" name="workflowRunId" value={filters.workflowRunId} onChange={setFilters} />
            <FilterInput label="Workflow step" name="workflowStepId" value={filters.workflowStepId} onChange={setFilters} />
            <FilterInput label="Correlation" name="correlationId" value={filters.correlationId} onChange={setFilters} />
            <FilterInput label="Causation" name="causationId" value={filters.causationId} onChange={setFilters} />
            <FilterInput label="Subject" name="subjectReferenceId" value={filters.subjectReferenceId} onChange={setFilters} />
            <FilterInput label="Event kind" name="eventKind" value={filters.eventKind} onChange={setFilters} />
            <FilterInput label="Source component" name="sourceComponent" value={filters.sourceComponent} onChange={setFilters} />
            <FilterInput label="From UTC" name="fromUtc" value={filters.fromUtc} onChange={setFilters} />
            <FilterInput label="To UTC" name="toUtc" value={filters.toUtc} onChange={setFilters} />
            <FilterInput label="Take" name="take" value={filters.take} onChange={setFilters} />
          </div>
          <div className="governance-timeline-actions">
            <button type="button" className="command-button command-button--primary" onClick={() => void search()} disabled={status === 'loading'}>
              Search
            </button>
            <button type="button" className="command-button" onClick={() => void search()} disabled={status === 'loading'}>
              Refresh
            </button>
            <button type="button" className="command-button command-button--subtle" onClick={clearFilters}>
              Clear Filters
            </button>
          </div>
        </Surface>

        <div className="governance-timeline-layout">
          <Surface testId="governance-timeline.list">
            <div className="section-heading">
              <p className="eyebrow">Timeline</p>
              <h3>Trace list</h3>
            </div>
            <p className="state-muted" data-testid="governance-timeline.status">
              {message}
            </p>

            {status === 'validation' ? (
              <IssueList issues={issues} testId="governance-timeline.validationError" />
            ) : status === 'empty' || traces.length === 0 ? (
              <EmptyState title="No governance traces" body={message} />
            ) : (
              <div className="governance-timeline-items" data-testid="governance-timeline.items">
                {traces.map((trace, index) => (
                  <button
                    key={trace.traceId ?? `${trace.correlationId ?? 'trace'}-${index}`}
                    type="button"
                    className={`governance-timeline-card ${safeText(trace.traceId) === selectedTraceId ? 'governance-timeline-card--selected' : ''}`}
                    data-testid="governance-timeline.item"
                    onClick={() => {
                      setSelectedTraceId(safeText(trace.traceId));
                      setDetail(null);
                      setDetailMessage('Open a trace to inspect safe timeline references.');
                    }}
                  >
                    <span className="governance-timeline-card__kind">{safeText(trace.eventKind, 'event')}</span>
                    <strong>{safeText(trace.safeSummary, 'No safe summary returned.')}</strong>
                    <span>{DateTimeDisplay.toLocalDisplay(trace.recordedUtc)}</span>
                    <span>Trace {safeText(trace.traceId)}</span>
                  </button>
                ))}
              </div>
            )}
          </Surface>

          <Surface testId="governance-timeline.detail">
            <div className="section-heading">
              <p className="eyebrow">Selected trace</p>
              <h3>{selectedTraceId ?? 'No trace selected'}</h3>
            </div>

            {selectedTrace ? (
              <div className="governance-timeline-detail">
                <MetadataRow label="Summary" value={safeText(selectedTrace.safeSummary, 'No safe summary returned.')} />
                <MetadataRow label="Trace" value={safeText(selectedTrace.traceId)} />
                <MetadataRow label="Correlation" value={safeText(selectedTrace.correlationId)} />
                <MetadataRow label="Causation" value={safeText(selectedTrace.causationId)} />
                <MetadataRow label="Workflow run" value={safeText(selectedTrace.workflowRunId)} />
                <MetadataRow label="Workflow step" value={safeText(selectedTrace.workflowStepId)} />
                <MetadataRow label="Subject" value={safeText(selectedTrace.subjectReferenceId)} />
                <MetadataRow label="Source" value={safeText(selectedTrace.sourceComponent)} />
                <MetadataRow label="Recorded" value={DateTimeDisplay.toLocalDisplay(selectedTrace.recordedUtc)} />
                <MetadataRow label="Boundary" value={<StatusBadge status="info">read-only evidence</StatusBadge>} />
              </div>
            ) : (
              <EmptyState title="Select a trace" body="Timeline observation is not approval or mutation permission." />
            )}

            <div className="governance-timeline-actions">
              <button type="button" className="command-button" onClick={copyReference} disabled={!selectedTrace}>
                Copy Reference
              </button>
              <button type="button" className="command-button" onClick={() => void openTrace()} disabled={!selectedTrace || status === 'loading'}>
                Open Trace
              </button>
            </div>
            {copyMessage ? <p className="state-muted" data-testid="governance-timeline.copyStatus">{copyMessage}</p> : null}
            <p className="state-muted" data-testid="governance-timeline.detailStatus">{detailMessage}</p>

            <TraceDetailPanel detail={detail} />
          </Surface>

          <Surface testId="governance-timeline.related">
            <div className="section-heading">
              <p className="eyebrow">Related reports</p>
              <h3>Read-only links</h3>
            </div>
            <div className="governance-timeline-actions governance-timeline-actions--stack">
              <button type="button" className="command-button" onClick={() => setRelatedMessage(relatedDiagnosisMessage(selectedTrace))}>
                Open Diagnosis
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(relatedCorrelationMessage(selectedTrace))}>
                Open Correlation Report
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(relatedAgentHealthMessage(selectedTrace))}>
                Open Agent Health
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(relatedBackendHealthMessage(selectedTrace))}>
                Open Backend Health
              </button>
            </div>
            <p className="state-muted" data-testid="governance-timeline.relatedStatus">
              {relatedMessage}
            </p>
            <p className="governance-timeline-boundary" data-testid="governance-timeline.boundaryFooter">
              This UI cannot approve, execute, retry, repair, transition workflow, invoke tools, dispatch agents, apply source, or clean up data.
            </p>
          </Surface>
        </div>
      </section>
    </main>
  );
}

function FilterInput({
  label,
  name,
  value,
  onChange
}: {
  label: string;
  name: keyof GovernanceTimelineFilters;
  value: string;
  onChange: (updater: (current: GovernanceTimelineFilters) => GovernanceTimelineFilters) => void;
}) {
  const id = `governance-timeline.${name}`;
  return (
    <label className="field-stack" htmlFor={id}>
      <span>{label}</span>
      <input
        id={id}
        data-testid={id}
        value={value}
        onChange={(event) => {
          const nextValue = event.currentTarget.value;
          onChange((current) => ({ ...current, [name]: nextValue }));
        }}
      />
    </label>
  );
}

function IssueList({ issues, testId }: { issues: GovernanceTraceIssue[]; testId: string }) {
  return (
    <div className="state-panel" data-testid={testId}>
      <h3>Validation error</h3>
      {issues.length === 0 ? (
        <p>The request was rejected by governance trace validation.</p>
      ) : (
        <ul className="detail-list">
          {issues.map((issue, index) => (
            <li key={`${issue.code ?? 'issue'}-${issue.field ?? 'field'}-${index}`}>
              {safeText(issue.field, 'query')}: {safeText(issue.message, 'Invalid governance timeline request.')}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function TraceDetailPanel({ detail }: { detail: GovernanceTraceDetail | null }) {
  if (!detail) {
    return null;
  }

  const timeline = sanitizeTimelineItems(detail.timeline ?? []);
  const related = detail.relatedReferences ?? [];

  return (
    <div className="governance-timeline-detail-grid" data-testid="governance-timeline.safeDetail">
      <div className="workflow-section">
        <h3>Safe event timeline</h3>
        {timeline.length === 0 ? (
          <p>No safe event timeline was returned for this trace.</p>
        ) : (
          <ul className="detail-list">
            {timeline.map((item, index) => (
              <li key={`${item.eventId ?? 'event'}-${index}`}>
                <strong>{safeText(item.eventKind, 'event')}</strong>: {safeText(item.safeSummary, 'No safe summary returned.')}
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className="workflow-section">
        <h3>Related references</h3>
        {related.length === 0 ? (
          <p>No related references were returned.</p>
        ) : (
          <ul className="detail-list">
            {related.map((reference, index) => (
              <li key={`${reference.referenceKind ?? 'reference'}-${reference.referenceId ?? index}`}>
                <strong>{safeText(reference.referenceKind, 'reference')}</strong> {safeText(reference.referenceId)} -{' '}
                {safeText(reference.safeSummary, 'No safe summary returned.')}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function toTraceQuery(filters: GovernanceTimelineFilters): GovernanceTraceQuery {
  const take = Number.parseInt(filters.take, 10);
  return {
    projectReferenceId: filters.projectReferenceId.trim(),
    workflowRunId: filters.workflowRunId.trim(),
    workflowStepId: filters.workflowStepId.trim(),
    correlationId: filters.correlationId.trim(),
    causationId: filters.causationId.trim(),
    subjectReferenceId: filters.subjectReferenceId.trim(),
    eventKind: filters.eventKind.trim(),
    sourceComponent: filters.sourceComponent.trim(),
    fromUtc: filters.fromUtc.trim(),
    toUtc: filters.toUtc.trim(),
    take: Number.isFinite(take) ? take : 50
  };
}

function sanitizeTraceSummaries(traces: GovernanceTraceSummary[]) {
  return traces.map((trace) => ({
    traceId: safeText(trace.traceId),
    projectReferenceId: safeText(trace.projectReferenceId),
    workflowRunId: safeText(trace.workflowRunId),
    workflowStepId: safeText(trace.workflowStepId),
    correlationId: safeText(trace.correlationId),
    causationId: safeText(trace.causationId),
    subjectReferenceId: safeText(trace.subjectReferenceId),
    eventKind: safeText(trace.eventKind),
    sourceComponent: safeText(trace.sourceComponent),
    safeSummary: safeText(trace.safeSummary, 'No safe summary returned.'),
    recordedUtc: safeText(trace.recordedUtc)
  }));
}

function sanitizeTraceDetail(detail: GovernanceTraceDetail | null): GovernanceTraceDetail | null {
  if (!detail) {
    return null;
  }

  return {
    summary: detail.summary ? sanitizeTraceSummaries([detail.summary])[0] : null,
    timeline: sanitizeTimelineItems(detail.timeline ?? []),
    relatedReferences: (detail.relatedReferences ?? []).map((reference) => ({
      referenceKind: safeText(reference.referenceKind),
      referenceId: safeText(reference.referenceId),
      safeSummary: safeText(reference.safeSummary, 'No safe summary returned.')
    })),
    boundaryWarnings: (detail.boundaryWarnings ?? []).map((warning) => safeText(warning)).filter(Boolean)
  };
}

function sanitizeTimelineItems(items: GovernanceTraceTimelineItem[]) {
  return items.map((item) => ({
    eventId: safeText(item.eventId),
    eventKind: safeText(item.eventKind),
    sourceComponent: safeText(item.sourceComponent),
    safeSummary: safeText(item.safeSummary, 'No safe summary returned.'),
    recordedUtc: safeText(item.recordedUtc),
    correlationId: safeText(item.correlationId),
    causationId: safeText(item.causationId),
    subjectReferenceId: safeText(item.subjectReferenceId)
  }));
}

function extractIssues(error: unknown): GovernanceTraceIssue[] {
  if (!(error instanceof IronDevApiError)) {
    return [];
  }

  const body = error.body as Partial<GovernanceTraceApiEnvelope<GovernanceTraceListData | GovernanceTraceDetailData>> | undefined;
  return [...(body?.errors ?? []), ...(body?.data?.issues ?? [])];
}

function safeText(value: string | number | null | undefined, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = text.toLowerCase();
  return unsafeTraceTextMarkers.some((marker) => normalized.includes(marker)) ? redactedTimelineText : text;
}

function relatedDiagnosisMessage(trace: GovernanceTraceSummary | null) {
  return trace?.workflowRunId
    ? `/api/v1/workflow/failures/${safeText(trace.workflowRunId)}/diagnosis-report`
    : 'Select a trace with a workflow run before opening diagnosis.';
}

function relatedCorrelationMessage(trace: GovernanceTraceSummary | null) {
  return trace?.correlationId
    ? `/api/v1/governance/correlation-reports/approval-gate-dogfood?correlationId=${safeText(trace.correlationId)}`
    : 'Select a trace with a correlation reference before opening correlation report.';
}

function relatedAgentHealthMessage(trace: GovernanceTraceSummary | null) {
  return trace?.workflowRunId
    ? `/api/v1/agents/runs/health-summary?workflowRunId=${safeText(trace.workflowRunId)}`
    : 'Select a trace with a workflow run before opening agent health.';
}

function relatedBackendHealthMessage(trace: GovernanceTraceSummary | null) {
  return trace?.projectReferenceId
    ? `/api/v1/operations/health?projectReferenceId=${safeText(trace.projectReferenceId)}`
    : '/api/v1/operations/health';
}
