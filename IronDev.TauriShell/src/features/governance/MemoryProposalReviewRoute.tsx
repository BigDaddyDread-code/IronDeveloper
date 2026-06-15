import { useCallback, useEffect, useMemo, useState } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  GovernanceTraceApiEnvelope,
  GovernanceTraceDetailData,
  GovernanceTraceIssue,
  GovernanceTraceListData,
  GovernanceTraceQuery,
  GovernanceTraceSummary
} from '../../api/types';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { useSessionContext } from '../../state/useSessionContext';
import type {
  MemoryProposalDetail,
  MemoryProposalListItem,
  MemoryProposalReviewFilters,
  MemoryProposalReviewLoadStatus
} from './MemoryProposalReviewTypes';
import {
  looksLikeMemoryProposal,
  proposalDetailFromTrace,
  proposalItemFromTrace
} from './MemoryProposalReviewTypes';

interface MemoryProposalReviewRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const emptyFilters: MemoryProposalReviewFilters = {
  projectReferenceId: '',
  workflowRunId: '',
  workflowStepId: '',
  memoryProposalId: '',
  correlationId: '',
  proposalStatus: '',
  memoryKind: '',
  scope: '',
  sourceComponent: '',
  fromUtc: '',
  toUtc: '',
  take: '50'
};

const boundaryWarnings = [
  'Read-only view',
  'Memory proposal is not accepted memory',
  'Proposed memory summary is not memory',
  'Memory review is not memory promotion',
  'Retrieval candidate is not retrieval activation'
];

const redactedMemoryProposalText = '[redacted memory proposal review text]';

const unsafeMemoryProposalTextMarkers = [
  'payload' + 'json',
  'raw' + 'payload',
  'memory' + 'payloadjson',
  'raw' + 'memorytext',
  'raw memory text',
  'raw' + 'prompt',
  'raw prompt',
  'raw' + 'completion',
  'raw completion',
  'raw' + 'tooloutput',
  'raw tool output',
  'raw' + 'commandoutput',
  'raw command output',
  'private' + 'reasoning',
  'private reasoning',
  'hidden' + 'reasoning',
  'hidden reasoning',
  'chain' + 'ofthought',
  'chain of thought',
  'scratch' + 'pad',
  'source' + 'content',
  'source file contents',
  'patch' + 'payload',
  'diff' + 'payload',
  'confidential' + 'clientdetail',
  'employer' + 'detail',
  'cred' + 'ential',
  'sec' + 'ret',
  'api' + 'key',
  'tok' + 'en',
  'bear' + 'er'
];

export function MemoryProposalReviewRoute({ onRouteReady }: MemoryProposalReviewRouteProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<MemoryProposalReviewFilters>(emptyFilters);
  const [status, setStatus] = useState<MemoryProposalReviewLoadStatus>('idle');
  const [message, setMessage] = useState('Set a project reference, workflow, proposal, or correlation filter, then search memory proposal evidence.');
  const [proposals, setProposals] = useState<MemoryProposalListItem[]>([]);
  const [selectedProposalId, setSelectedProposalId] = useState<string | null>(null);
  const [detail, setDetail] = useState<MemoryProposalDetail | null>(null);
  const [issues, setIssues] = useState<GovernanceTraceIssue[]>([]);
  const [warnings, setWarnings] = useState<string[]>(boundaryWarnings);

  const canQuery = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const safeText = useCallback((value: unknown, fallback = '') => {
    if (value === undefined || value === null) {
      return fallback;
    }

    const text = String(value);
    const normalized = text.toLowerCase().replace(/[\s_-]+/g, ' ').trim();
    const compact = normalized.replace(/\s+/g, '');
    const unsafe = unsafeMemoryProposalTextMarkers.some((marker) => {
      const markerNormalized = marker.toLowerCase().replace(/[\s_-]+/g, ' ').trim();
      const markerCompact = markerNormalized.replace(/\s+/g, '');
      return normalized.includes(markerNormalized) || compact.includes(markerCompact);
    });

    return unsafe ? redactedMemoryProposalText : text;
  }, []);

  const updateFilter = useCallback(
    (key: keyof MemoryProposalReviewFilters, value: string) => {
      setFilters((current) => ({ ...current, [key]: safeText(value) }));
    },
    [safeText]
  );

  const resetFilters = useCallback(() => {
    setFilters(emptyFilters);
    setProposals([]);
    setSelectedProposalId(null);
    setDetail(null);
    setIssues([]);
    setWarnings(boundaryWarnings);
    setStatus('idle');
    setMessage('Filters cleared. This did not accept, promote, write, or activate memory.');
  }, []);

  const search = useCallback(async () => {
    if (!canQuery) {
      setStatus('error');
      setMessage('Memory Proposal Review requires API connection and authentication.');
      return;
    }

    if (!hasSearchBasis(filters)) {
      setStatus('error');
      setMessage('Memory Proposal Review needs a project, workflow, proposal, source, or correlation filter.');
      return;
    }

    const controller = new AbortController();
    setStatus('loading');
    setMessage('Reading memory proposal evidence through GET-only governance trace endpoints.');

    try {
      const response = await loadProposalEvidence(filters, controller.signal);
      const traces = sanitizeTraceSummaries(response.data?.traces ?? []);
      const nextItems = traces
        .map((trace) => proposalItemFromTrace(trace, safeText))
        .filter((item) => looksLikeMemoryProposal(item))
        .filter((item) => matchesFilters(item, filters));

      setWarnings([
        ...boundaryWarnings,
        ...(response.warnings ?? []).map((warning) => safeText(warning)).filter(Boolean),
        ...(response.data?.boundaryWarnings ?? []).map((warning) => safeText(warning)).filter(Boolean)
      ]);
      setIssues([...(response.errors ?? []), ...(response.data?.issues ?? [])]);
      setProposals(nextItems);
      setSelectedProposalId(nextItems[0]?.memoryProposalId ?? null);
      setDetail(null);
      setStatus(nextItems.length > 0 ? 'ready' : 'empty');
      setMessage(
        nextItems.length > 0
          ? 'Memory proposal evidence loaded. Review is not promotion.'
          : 'No memory proposal evidence found for the selected filters.'
      );
    } catch (error) {
      setStatus('error');
      setMessage(error instanceof IronDevApiError ? error.message : 'Memory proposal evidence read failed.');
      setProposals([]);
      setDetail(null);
    }
  }, [canQuery, filters, safeText, session.client]);

  const openProposal = useCallback(
    async (proposal: MemoryProposalListItem) => {
      if (!canQuery) {
        setMessage('API connection is required before opening memory proposal evidence.');
        return;
      }

      setStatus('loading');
      setSelectedProposalId(proposal.memoryProposalId);

      try {
        const response = await loadProposalDetail(proposal, filters);
        const trace = sanitizeTraceDetail(response.data?.trace ?? null);
        setDetail(proposalDetailFromTrace(proposal, trace, safeText));
        setWarnings([
          ...boundaryWarnings,
          ...(response.warnings ?? []).map((warning: string) => safeText(warning)).filter(Boolean),
          ...(response.data?.trace?.boundaryWarnings ?? []).map((warning: string) => safeText(warning)).filter(Boolean)
        ]);
        setIssues([...(response.errors ?? [])]);
        setStatus('ready');
        setMessage('Memory proposal opened for read-only review. Copying or opening does not accept memory.');
      } catch (error) {
        setStatus('error');
        setMessage(error instanceof IronDevApiError ? error.message : 'Memory proposal detail read failed.');
      }
    },
    [canQuery, filters, safeText, session.client]
  );

  const copyValue = useCallback(
    async (label: string, value: string) => {
      const safeValue = safeText(value);
      if (!safeValue || safeValue === redactedMemoryProposalText) {
        setMessage(`${label} was not copied because it was unavailable or unsafe.`);
        return;
      }

      await navigator.clipboard?.writeText(safeValue);
      setMessage(`${label} copied. Copy proposal id is not acceptance.`);
    },
    [safeText]
  );

  const openRelated = useCallback((label: string, target: string) => {
    setMessage(`${label} read-only path: ${target}. Navigation is not memory activation.`);
  }, []);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: 'memory-proposals.search',
        label: 'Search',
        intent: 'primary',
        onExecute: search,
        disabled: status === 'loading',
        testId: 'memory-proposals.command.search'
      },
      {
        id: 'memory-proposals.refresh',
        label: 'Refresh',
        intent: 'secondary',
        onExecute: search,
        disabled: status === 'loading',
        testId: 'memory-proposals.command.refresh'
      },
      {
        id: 'memory-proposals.clear',
        label: 'Clear Filters',
        intent: 'ghost',
        onExecute: resetFilters,
        disabled: status === 'loading',
        testId: 'memory-proposals.command.clear'
      }
    ],
    [resetFilters, search, status]
  );

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: canQuery ? null : 'Memory Proposal Review requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: `${proposals.length} proposal(s)`, testId: 'memory-proposals.chip.count' },
        { label: 'Read-only', testId: 'memory-proposals.chip.readonly' }
      ]
    }),
    [canQuery, commands, proposals.length]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  return (
    <main className="memory-proposal-review-workspace" data-testid="memory-proposals.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div>
            <p className="eyebrow">Memory review desk</p>
            <h1>Memory Proposal Review</h1>
            <p className="lede">
              Inspect proposal-only memory evidence and scope warnings through existing GET-only governance reads.
            </p>
          </div>
          <StatusBadge status={status === 'error' ? 'danger' : status === 'loading' ? 'warning' : 'ready'}>
            {status === 'loading' ? 'Reading' : 'Read-only'}
          </StatusBadge>
        </div>

        <div className="memory-proposal-review-banner" data-testid="memory-proposals.readonly-banner">
          {boundaryWarnings.map((warning) => (
            <span key={warning}>{warning}</span>
          ))}
        </div>

        <Surface className="memory-proposal-review-filters" data-testid="memory-proposals.filters">
          <label>
            Project reference
            <input
              value={filters.projectReferenceId}
              onChange={(event) => updateFilter('projectReferenceId', event.currentTarget.value)}
              placeholder="Project reference"
            />
          </label>
          <label>
            Workflow run
            <input
              value={filters.workflowRunId}
              onChange={(event) => updateFilter('workflowRunId', event.currentTarget.value)}
              placeholder="Workflow run id"
            />
          </label>
          <label>
            Workflow step
            <input
              value={filters.workflowStepId}
              onChange={(event) => updateFilter('workflowStepId', event.currentTarget.value)}
              placeholder="Workflow step id"
            />
          </label>
          <label>
            Memory proposal id
            <input
              value={filters.memoryProposalId}
              onChange={(event) => updateFilter('memoryProposalId', event.currentTarget.value)}
              placeholder="Memory proposal id"
            />
          </label>
          <label>
            Correlation id
            <input
              value={filters.correlationId}
              onChange={(event) => updateFilter('correlationId', event.currentTarget.value)}
              placeholder="Correlation id"
            />
          </label>
          <label>
            Proposal status
            <input
              value={filters.proposalStatus}
              onChange={(event) => updateFilter('proposalStatus', event.currentTarget.value)}
              placeholder="Proposal status"
            />
          </label>
          <label>
            Memory kind
            <input
              value={filters.memoryKind}
              onChange={(event) => updateFilter('memoryKind', event.currentTarget.value)}
              placeholder="Memory kind"
            />
          </label>
          <label>
            Scope
            <input value={filters.scope} onChange={(event) => updateFilter('scope', event.currentTarget.value)} placeholder="Scope" />
          </label>
          <label>
            Source component
            <input
              value={filters.sourceComponent}
              onChange={(event) => updateFilter('sourceComponent', event.currentTarget.value)}
              placeholder="Source component"
            />
          </label>
          <label>
            From UTC
            <input
              value={filters.fromUtc}
              onChange={(event) => updateFilter('fromUtc', event.currentTarget.value)}
              placeholder="2026-01-01T00:00:00Z"
            />
          </label>
          <label>
            To UTC
            <input
              value={filters.toUtc}
              onChange={(event) => updateFilter('toUtc', event.currentTarget.value)}
              placeholder="2026-01-02T00:00:00Z"
            />
          </label>
          <label>
            Take
            <input value={filters.take} onChange={(event) => updateFilter('take', event.currentTarget.value)} placeholder="50" />
          </label>
          <div className="memory-proposal-review-filter-actions">
            <button type="button" onClick={search} disabled={status === 'loading'} data-testid="memory-proposals.search">
              Search
            </button>
            <button type="button" onClick={search} disabled={status === 'loading'} data-testid="memory-proposals.refresh">
              Refresh
            </button>
            <button type="button" onClick={resetFilters} disabled={status === 'loading'} data-testid="memory-proposals.clear">
              Clear Filters
            </button>
          </div>
        </Surface>

        <p className="memory-proposal-review-message" data-testid="memory-proposals.message">
          {message}
        </p>

        <div className="memory-proposal-review-grid">
          <Surface className="memory-proposal-review-panel">
            <div className="memory-proposal-review-panel__header">
              <h2>Memory proposals</h2>
              <span>{proposals.length} returned</span>
            </div>
            {proposals.length === 0 ? (
              <EmptyState title="No memory proposal evidence found" body="No memory proposal evidence found for the selected filters." />
            ) : (
              <div className="memory-proposal-review-list" data-testid="memory-proposals.list">
                {proposals.map((proposal) => (
                  <article
                    key={`${proposal.traceId}-${proposal.memoryProposalId}`}
                    className={
                      proposal.memoryProposalId === selectedProposalId
                        ? 'memory-proposal-review-card is-selected'
                        : 'memory-proposal-review-card'
                    }
                    data-testid="memory-proposals.item"
                  >
                    <div>
                      <h3>{proposal.memoryProposalId}</h3>
                      <p>{proposal.safeSummary}</p>
                    </div>
                    <StatusBadge status="neutral">{proposal.proposalStatus}</StatusBadge>
                    <dl>
                      <dt>Kind</dt>
                      <dd>{proposal.memoryKind}</dd>
                      <dt>Scope</dt>
                      <dd>{proposal.proposedScope}</dd>
                      <dt>Workflow</dt>
                      <dd>{proposal.workflowRunId}</dd>
                      <dt>Source</dt>
                      <dd>{proposal.sourceComponent}</dd>
                      <dt>Created</dt>
                      <dd>{displayDate(proposal.createdUtc)}</dd>
                    </dl>
                    <div className="memory-proposal-review-card__actions">
                      <button type="button" onClick={() => openProposal(proposal)} data-testid="memory-proposals.open-proposal">
                        Open Proposal
                      </button>
                      <button type="button" onClick={() => copyValue('Proposal ID', proposal.memoryProposalId)}>
                        Copy Proposal ID
                      </button>
                      <button type="button" onClick={() => copyValue('Correlation ID', proposal.correlationId)}>
                        Copy Correlation ID
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </Surface>

          <Surface className="memory-proposal-review-detail" data-testid="memory-proposals.detail">
            <div className="memory-proposal-review-panel__header">
              <h2>Proposal detail</h2>
              <span>Safe summaries only</span>
            </div>
            {detail ? (
              <div className="memory-proposal-review-detail-grid">
                <DetailBlock detail={detail} />
                <ReferenceList title="Evidence references" references={detail.evidenceReferences} testId="memory-proposals.evidence" />
                <ReferenceList title="Related traces" references={detail.relatedTraces} testId="memory-proposals.related-traces" />
                <WarningList title="Confidentiality warnings" warnings={detail.confidentialityWarnings} testId="memory-proposals.confidentiality" />
                <WarningList title="Portability warnings" warnings={detail.portabilityWarnings} testId="memory-proposals.portability" />
                <WarningList title="Missing evidence warnings" warnings={detail.missingEvidenceWarnings} testId="memory-proposals.missing-evidence" />
                <WarningList title="Boundary warnings" warnings={[...detail.boundaryWarnings, ...warnings]} testId="memory-proposals.boundary" />
                <div>
                  <h3>Related read-only links</h3>
                  <div className="memory-proposal-review-related">
                    <button type="button" onClick={() => openRelated('Open Trace', tracePath(detail))}>
                      Open Trace
                    </button>
                    <button type="button" onClick={() => openRelated('Open Timeline', timelinePath(detail))}>
                      Open Timeline
                    </button>
                    <button type="button" onClick={() => openRelated('Open Workflow', workflowPath(detail))}>
                      Open Workflow
                    </button>
                  </div>
                </div>
              </div>
            ) : (
              <EmptyState title="No proposal opened" body="Open a proposal to inspect safe evidence and warnings." />
            )}
          </Surface>
        </div>

        {issues.length > 0 ? (
          <Surface className="memory-proposal-review-issues">
            <div data-testid="memory-proposals.issues">
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

        <footer className="memory-proposal-review-footer" data-testid="memory-proposals.footer">
          This UI cannot accept memory, promote memory, write memory, activate retrieval, approve cross-project learning,
          transition workflow, invoke tools, dispatch agents, apply source, or release software.
        </footer>
      </section>
    </main>
  );

  async function loadProposalEvidence(current: MemoryProposalReviewFilters, signal: AbortSignal) {
    const projectReferenceId = current.projectReferenceId.trim();
    const workflowRunId = current.workflowRunId.trim();
    const correlationId = current.correlationId.trim();

    if (workflowRunId && projectReferenceId) {
      return session.client.getGovernanceTraceByWorkflowRun(workflowRunId, projectReferenceId, signal);
    }

    if (correlationId) {
      const response = await session.client.getGovernanceTraceByCorrelation(correlationId, projectReferenceId, signal);
      return detailResponseAsList(response);
    }

    return session.client.searchGovernanceTraces(toTraceQuery(current), signal);
  }

  async function loadProposalDetail(proposal: MemoryProposalListItem, current: MemoryProposalReviewFilters) {
    if (proposal.traceId) {
      return session.client.getGovernanceTrace(proposal.traceId);
    }

    if (proposal.correlationId) {
      return session.client.getGovernanceTraceByCorrelation(proposal.correlationId, current.projectReferenceId.trim());
    }

    const list = await session.client.searchGovernanceTraces(toTraceQuery(current));
    const firstTrace = list.data?.traces?.[0] ?? null;
    return {
      status: list.status,
      data: firstTrace ? { trace: { summary: firstTrace, timeline: [], relatedReferences: [], boundaryWarnings: list.data?.boundaryWarnings ?? [] } } : null,
      mutationOccurred: list.mutationOccurred,
      boundary: list.boundary,
      warnings: list.warnings,
      errors: list.errors
    } satisfies GovernanceTraceApiEnvelope<GovernanceTraceDetailData>;
  }

  function detailResponseAsList(response: GovernanceTraceApiEnvelope<GovernanceTraceDetailData>) {
    const summary = response.data?.trace?.summary;
    return {
      ...response,
      data: {
        status: response.data?.status,
        traces: summary ? [summary] : [],
        issues: response.data?.issues ?? [],
        boundaryWarnings: response.data?.boundaryWarnings ?? response.data?.trace?.boundaryWarnings ?? []
      }
    } satisfies GovernanceTraceApiEnvelope<GovernanceTraceListData>;
  }

  function sanitizeTraceSummaries(traces: GovernanceTraceSummary[]) {
    return traces.map((trace) => ({
      ...trace,
      traceId: safeText(trace.traceId),
      projectReferenceId: safeText(trace.projectReferenceId),
      workflowRunId: safeText(trace.workflowRunId),
      workflowStepId: safeText(trace.workflowStepId),
      correlationId: safeText(trace.correlationId),
      causationId: safeText(trace.causationId),
      subjectReferenceId: safeText(trace.subjectReferenceId),
      eventKind: safeText(trace.eventKind),
      sourceComponent: safeText(trace.sourceComponent),
      safeSummary: safeText(trace.safeSummary),
      recordedUtc: safeText(trace.recordedUtc)
    }));
  }

  function sanitizeTraceDetail(trace: GovernanceTraceDetailData['trace']) {
    if (!trace) {
      return null;
    }

    return {
      summary: trace.summary ? sanitizeTraceSummaries([trace.summary])[0] : null,
      timeline: (trace.timeline ?? []).map((entry) => ({
        ...entry,
        eventId: safeText(entry.eventId),
        eventKind: safeText(entry.eventKind),
        sourceComponent: safeText(entry.sourceComponent),
        safeSummary: safeText(entry.safeSummary),
        recordedUtc: safeText(entry.recordedUtc),
        correlationId: safeText(entry.correlationId),
        causationId: safeText(entry.causationId),
        subjectReferenceId: safeText(entry.subjectReferenceId)
      })),
      relatedReferences: (trace.relatedReferences ?? []).map((reference) => ({
        ...reference,
        referenceKind: safeText(reference.referenceKind),
        referenceId: safeText(reference.referenceId),
        safeSummary: safeText(reference.safeSummary)
      })),
      boundaryWarnings: (trace.boundaryWarnings ?? []).map((warning) => safeText(warning)).filter(Boolean)
    };
  }
}

function hasSearchBasis(filters: MemoryProposalReviewFilters) {
  return [
    filters.projectReferenceId,
    filters.workflowRunId,
    filters.workflowStepId,
    filters.memoryProposalId,
    filters.correlationId,
    filters.sourceComponent
  ].some((value) => String(value).trim());
}

function toTraceQuery(filters: MemoryProposalReviewFilters): GovernanceTraceQuery {
  return {
    projectReferenceId: filters.projectReferenceId.trim() || undefined,
    workflowRunId: filters.workflowRunId.trim() || undefined,
    workflowStepId: filters.workflowStepId.trim() || undefined,
    correlationId: filters.correlationId.trim() || undefined,
    subjectReferenceId: filters.memoryProposalId.trim() || undefined,
    sourceComponent: filters.sourceComponent.trim() || undefined,
    fromUtc: filters.fromUtc.trim() || undefined,
    toUtc: filters.toUtc.trim() || undefined,
    take: boundedTake(filters.take)
  };
}

function matchesFilters(item: MemoryProposalListItem, filters: MemoryProposalReviewFilters) {
  return (
    textContains(item.memoryProposalId, filters.memoryProposalId) &&
    textContains(item.proposalStatus, filters.proposalStatus) &&
    textContains(item.memoryKind, filters.memoryKind) &&
    textContains(item.proposedScope, filters.scope) &&
    textContains(item.sourceComponent, filters.sourceComponent) &&
    dateWithin(item.createdUtc, filters.fromUtc, filters.toUtc)
  );
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

function DetailBlock({ detail }: { detail: MemoryProposalDetail }) {
  return (
    <div data-testid="memory-proposals.safe-detail">
      <h3>{detail.memoryProposalId}</h3>
      <p>{detail.safeSummary}</p>
      <dl>
        <div>
          <dt>Kind</dt>
          <dd>{detail.memoryKind}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>{detail.proposalStatus}</dd>
        </div>
        <div>
          <dt>Scope</dt>
          <dd>{detail.proposedScope}</dd>
        </div>
        <div>
          <dt>Confidence</dt>
          <dd>{detail.confidence}</dd>
        </div>
        <div>
          <dt>Rationale</dt>
          <dd>{detail.safeRationale}</dd>
        </div>
        <div>
          <dt>Workflow</dt>
          <dd>{detail.workflowRunId}</dd>
        </div>
        <div>
          <dt>Step</dt>
          <dd>{detail.workflowStepId}</dd>
        </div>
        <div>
          <dt>Correlation</dt>
          <dd>{detail.correlationId}</dd>
        </div>
      </dl>
    </div>
  );
}

function ReferenceList({
  title,
  references,
  testId
}: {
  title: string;
  references: { referenceKind: string; referenceId: string; safeSummary: string }[];
  testId: string;
}) {
  return (
    <div data-testid={testId}>
      <h3>{title}</h3>
      {references.length === 0 ? (
        <p>No safe references returned.</p>
      ) : (
        <ul>
          {references.map((reference) => (
            <li key={`${reference.referenceKind}-${reference.referenceId}`}>
              <strong>{reference.referenceKind}</strong>
              <span>{reference.referenceId}</span>
              <p>{reference.safeSummary}</p>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function WarningList({ title, warnings, testId }: { title: string; warnings: string[]; testId: string }) {
  return (
    <div data-testid={testId}>
      <h3>{title}</h3>
      {warnings.length === 0 ? (
        <p>No warnings returned.</p>
      ) : (
        <ul>
          {warnings.map((warning, index) => (
            <li key={`${warning}-${index}`}>{warning}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

function tracePath(detail: MemoryProposalDetail) {
  return `/governance/timeline?correlationId=${encodeURIComponent(detail.correlationId)}`;
}

function timelinePath(detail: MemoryProposalDetail) {
  return tracePath(detail);
}

function workflowPath(detail: MemoryProposalDetail) {
  return `/workflows/runs?workflowRunId=${encodeURIComponent(detail.workflowRunId)}&workflowStepId=${encodeURIComponent(
    detail.workflowStepId
  )}`;
}
