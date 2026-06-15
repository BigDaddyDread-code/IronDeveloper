import { useCallback, useEffect, useMemo, useState } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ToolGateApiEnvelope,
  ToolGateDecisionListItem,
  ToolGateIssue,
  ToolRequestListItem
} from '../../api/types';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import { useSessionContext } from '../../state/useSessionContext';
import type { ToolGateLoadStatus } from './ToolGateDecisionTypes';

interface ToolGateDecisionRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

interface ToolGateFilters {
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  toolRequestId: string;
  gateDecisionId: string;
  correlationId: string;
  decisionStatus: string;
  toolName: string;
  sourceComponent: string;
  fromUtc: string;
  toUtc: string;
  take: string;
}

const emptyFilters: ToolGateFilters = {
  projectReferenceId: '',
  workflowRunId: '',
  workflowStepId: '',
  toolRequestId: '',
  gateDecisionId: '',
  correlationId: '',
  decisionStatus: '',
  toolName: '',
  sourceComponent: '',
  fromUtc: '',
  toUtc: '',
  take: '50'
};

const boundaryWarnings = [
  'Read-only view',
  'Tool request visibility is not tool execution',
  'Gate decision visibility is not gate authority',
  'Approval requirement is not approval',
  'Policy evidence is not policy satisfaction'
];

const redactedToolGateText = '[redacted tool gate text]';

const unsafeToolGateTextMarkers = [
  'payload' + 'json',
  'raw' + 'payload',
  'request' + 'payloadjson',
  'decision' + 'payloadjson',
  'tool' + 'inputjson',
  'tool input json',
  'tool' + 'outputjson',
  'tool output json',
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

export function ToolGateDecisionRoute({ route, onRouteReady }: ToolGateDecisionRouteProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<ToolGateFilters>(emptyFilters);
  const [status, setStatus] = useState<ToolGateLoadStatus>('idle');
  const [message, setMessage] = useState('Enter a project reference plus a tool request id or gate decision id, then search.');
  const [requests, setRequests] = useState<ToolRequestListItem[]>([]);
  const [decisions, setDecisions] = useState<ToolGateDecisionListItem[]>([]);
  const [selectedRequestId, setSelectedRequestId] = useState<string | null>(null);
  const [selectedDecisionId, setSelectedDecisionId] = useState<string | null>(null);
  const [issues, setIssues] = useState<ToolGateIssue[]>([]);
  const [detailMessage, setDetailMessage] = useState('Open a request or decision to inspect safe evidence references.');
  const [relatedMessage, setRelatedMessage] = useState('Related links stay read-only and do not continue workflow.');
  const [copyMessage, setCopyMessage] = useState('');

  const selectedRequest = useMemo(
    () => requests.find((request) => request.toolRequestId === selectedRequestId) ?? requests[0] ?? null,
    [requests, selectedRequestId]
  );
  const selectedDecision = useMemo(
    () => decisions.find((decision) => decision.decisionId === selectedDecisionId) ?? decisions[0] ?? null,
    [decisions, selectedDecisionId]
  );

  const canQuery = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const search = useCallback(async () => {
    if (!canQuery) {
      setStatus('error');
      setMessage('Connect IronDev.Api and sign in before loading tool request or gate decision evidence.');
      setRequests([]);
      setDecisions([]);
      setIssues([]);
      return;
    }

    const projectId = filters.projectReferenceId.trim();
    const toolRequestId = filters.toolRequestId.trim();
    const gateDecisionId = filters.gateDecisionId.trim();

    if (!projectId) {
      setStatus('validation');
      setMessage('Tool request and gate decision search needs a project reference.');
      setIssues([{ code: 'missing_project_reference', field: 'projectReferenceId', message: 'Project reference is required for read-only inspection.' }]);
      setRequests([]);
      setDecisions([]);
      return;
    }

    if (!toolRequestId && !gateDecisionId) {
      setStatus('empty');
      setMessage('No tool request or gate decision evidence found for the selected filters.');
      setIssues([]);
      setRequests([]);
      setDecisions([]);
      return;
    }

    setStatus('loading');
    setMessage('Loading tool request and gate decision evidence...');
    setIssues([]);

    const controller = new AbortController();

    try {
      const nextRequests: ToolRequestListItem[] = [];
      const nextDecisions: ToolGateDecisionListItem[] = [];
      const nextIssues: ToolGateIssue[] = [];

      if (toolRequestId) {
        const response = await session.client.getToolRequest(toolRequestId, projectId, controller.signal);
        nextIssues.push(...extractEnvelopeIssues(response));
        const request = coerceToolRequest(response.data ?? response);
        if (request) {
          nextRequests.push(request);
        }
      }

      if (gateDecisionId) {
        const response = await session.client.getToolGateDecision(gateDecisionId, projectId, controller.signal);
        nextIssues.push(...extractEnvelopeIssues(response));
        const decision = coerceToolDecision(response.data ?? response);
        if (decision) {
          nextDecisions.push(decision);
        }
      }

      setRequests(nextRequests);
      setDecisions(nextDecisions);
      setSelectedRequestId(nextRequests[0]?.toolRequestId ?? null);
      setSelectedDecisionId(nextDecisions[0]?.decisionId ?? null);
      setIssues(nextIssues);
      setStatus(nextRequests.length === 0 && nextDecisions.length === 0 ? 'empty' : 'loaded');
      setMessage(
        nextRequests.length === 0 && nextDecisions.length === 0
          ? 'No tool request or gate decision evidence found for the selected filters.'
          : `Loaded ${nextRequests.length} request(s) and ${nextDecisions.length} gate decision(s).`
      );
    } catch (error) {
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setRequests([]);
      setDecisions([]);
      setSelectedRequestId(null);
      setSelectedDecisionId(null);
      setStatus(nextIssues.length > 0 ? 'validation' : 'error');
      setMessage(nextIssues.length > 0 ? 'Tool request or gate decision request was rejected by validation.' : 'Tool request or gate decision request failed.');
    } finally {
      controller.abort();
    }
  }, [canQuery, filters.gateDecisionId, filters.projectReferenceId, filters.toolRequestId, session.client]);

  const clearFilters = useCallback(() => {
    setFilters(emptyFilters);
    setIssues([]);
    setRequests([]);
    setDecisions([]);
    setSelectedRequestId(null);
    setSelectedDecisionId(null);
    setMessage('Filters cleared. Search is read-only and does not replay governance.');
  }, []);

  const copyRequestId = useCallback(() => {
    if (!selectedRequest) {
      setCopyMessage('Select a request before copying an id.');
      return;
    }

    void navigator.clipboard?.writeText(selectedRequest.toolRequestId).catch(() => undefined);
    setCopyMessage('Request id copied for inspection only. Copy request id is not approval.');
  }, [selectedRequest]);

  const copyDecisionId = useCallback(() => {
    if (!selectedDecision) {
      setCopyMessage('Select a decision before copying an id.');
      return;
    }

    void navigator.clipboard?.writeText(selectedDecision.decisionId).catch(() => undefined);
    setCopyMessage('Decision id copied for inspection only. Copy decision id is not policy satisfaction.');
  }, [selectedDecision]);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: `workspace.${route.id}.toolGates.refresh`,
        label: status === 'loading' ? 'Refreshing gate ledger' : 'Refresh gate ledger',
        intent: 'secondary',
        onExecute: () => void search(),
        disabled: status === 'loading',
        busy: status === 'loading',
        testId: 'tool-gates.command.refresh',
        disabledReason: canQuery ? undefined : 'Connect IronDev.Api and sign in before refreshing tool request evidence.'
      }
    ],
    [canQuery, route.id, search, status]
  );

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: canQuery ? null : 'Tool request and gate decision UI requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: `${requests.length} request(s)`, testId: 'tool-gates.chip.requests' },
        { label: `${decisions.length} decision(s)`, testId: 'tool-gates.chip.decisions' },
        { label: 'Read-only', testId: 'tool-gates.chip.readonly' }
      ],
      blockReasonTestId: canQuery ? undefined : 'tool-gates.blockedReason'
    }),
    [canQuery, commands, decisions.length, requests.length]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  return (
    <main className="tool-gate-workspace" data-testid="tool-gates.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Gate ledger viewer</p>
            <h2>Tool Requests and Gate Decisions</h2>
            <p>
              Inspect safe tool request and gate decision evidence using existing read-only API endpoints. The page is a window,
              not a control panel.
            </p>
          </div>
          <div className="tool-gate-banner" data-testid="tool-gates.readonlyBanner">
            {boundaryWarnings.map((warning) => (
              <span key={warning}>{warning}</span>
            ))}
          </div>
        </div>

        <Surface testId="tool-gates.filters" className="tool-gate-filters">
          <div className="section-heading">
            <p className="eyebrow">Filters</p>
            <h3>Read-only lookup</h3>
          </div>
          <div className="tool-gate-filter-grid">
            <FilterInput label="Project reference" name="projectReferenceId" value={filters.projectReferenceId} onChange={setFilters} />
            <FilterInput label="Workflow run" name="workflowRunId" value={filters.workflowRunId} onChange={setFilters} />
            <FilterInput label="Workflow step" name="workflowStepId" value={filters.workflowStepId} onChange={setFilters} />
            <FilterInput label="Tool request id" name="toolRequestId" value={filters.toolRequestId} onChange={setFilters} />
            <FilterInput label="Gate decision id" name="gateDecisionId" value={filters.gateDecisionId} onChange={setFilters} />
            <FilterInput label="Correlation" name="correlationId" value={filters.correlationId} onChange={setFilters} />
            <FilterInput label="Decision status" name="decisionStatus" value={filters.decisionStatus} onChange={setFilters} />
            <FilterInput label="Tool name" name="toolName" value={filters.toolName} onChange={setFilters} />
            <FilterInput label="Source component" name="sourceComponent" value={filters.sourceComponent} onChange={setFilters} />
            <FilterInput label="From UTC" name="fromUtc" value={filters.fromUtc} onChange={setFilters} />
            <FilterInput label="To UTC" name="toUtc" value={filters.toUtc} onChange={setFilters} />
            <FilterInput label="Take" name="take" value={filters.take} onChange={setFilters} />
          </div>
          <div className="tool-gate-actions">
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

        <div className="tool-gate-layout">
          <Surface testId="tool-gates.requests">
            <div className="section-heading">
              <p className="eyebrow">Requests</p>
              <h3>Tool request list</h3>
            </div>
            <p className="state-muted" data-testid="tool-gates.status">{message}</p>
            {status === 'validation' ? (
              <IssueList issues={issues} testId="tool-gates.validationError" />
            ) : requests.length === 0 ? (
              <EmptyState title="No tool request evidence" body="No tool request or gate decision evidence found for the selected filters." />
            ) : (
              <div className="tool-gate-card-list" data-testid="tool-gates.requestItems">
                {requests.map((request) => (
                  <button
                    key={request.toolRequestId}
                    type="button"
                    className={`tool-gate-card ${request.toolRequestId === selectedRequestId ? 'tool-gate-card--selected' : ''}`}
                    data-testid="tool-gates.requestItem"
                    onClick={() => setSelectedRequestId(request.toolRequestId)}
                  >
                    <span className="tool-gate-card__kind">{safeText(request.requestStatus, 'request')}</span>
                    <strong>{safeText(request.requestedToolName, 'Tool unavailable')}</strong>
                    <span>{safeText(request.safeSummary, 'No safe summary returned.')}</span>
                    <span>{safeText(request.toolRequestId)}</span>
                  </button>
                ))}
              </div>
            )}
          </Surface>

          <Surface testId="tool-gates.decisions">
            <div className="section-heading">
              <p className="eyebrow">Gate decisions</p>
              <h3>Decision list</h3>
            </div>
            {decisions.length === 0 ? (
              <EmptyState title="No gate decision evidence" body="Search with a gate decision id to inspect read-only gate evidence." />
            ) : (
              <div className="tool-gate-card-list" data-testid="tool-gates.decisionItems">
                {decisions.map((decision) => (
                  <button
                    key={decision.decisionId}
                    type="button"
                    className={`tool-gate-card ${decision.decisionId === selectedDecisionId ? 'tool-gate-card--selected' : ''}`}
                    data-testid="tool-gates.decisionItem"
                    onClick={() => setSelectedDecisionId(decision.decisionId)}
                  >
                    <span className="tool-gate-card__kind">{safeText(decision.decisionStatus, 'decision')}</span>
                    <strong>{safeText(decision.safeSummary, 'No safe summary returned.')}</strong>
                    <span>{safeText(decision.safeReason, 'No safe reason returned.')}</span>
                    <span>{safeText(decision.decisionId)}</span>
                  </button>
                ))}
              </div>
            )}
          </Surface>

          <Surface testId="tool-gates.detail">
            <div className="section-heading">
              <p className="eyebrow">Selected evidence</p>
              <h3>Safe detail</h3>
            </div>
            <div className="tool-gate-detail-grid" data-testid="tool-gates.safeDetail">
              <div className="workflow-section">
                <h3>Request</h3>
                {selectedRequest ? (
                  <>
                    <MetadataRow label="Request id" value={safeText(selectedRequest.toolRequestId)} />
                    <MetadataRow label="Tool" value={safeText(selectedRequest.requestedToolName)} />
                    <MetadataRow label="Capability" value={safeText(selectedRequest.requestedCapability)} />
                    <MetadataRow label="Operation" value={safeText(selectedRequest.requestedOperation)} />
                    <MetadataRow label="Status" value={<StatusBadge status="info">{safeText(selectedRequest.requestStatus)}</StatusBadge>} />
                    <MetadataRow label="Workflow run" value={safeText(selectedRequest.workflowRunId)} />
                    <MetadataRow label="Workflow step" value={safeText(selectedRequest.workflowStepId)} />
                    <MetadataRow label="Source component" value={safeText(selectedRequest.sourceComponent)} />
                    <MetadataRow label="Created" value={DateTimeDisplay.toLocalDisplay(selectedRequest.createdUtc)} />
                    <MetadataRow label="Summary" value={safeText(selectedRequest.safeSummary, 'No safe summary returned.')} />
                  </>
                ) : (
                  <p>No selected request.</p>
                )}
              </div>
              <div className="workflow-section">
                <h3>Decision</h3>
                {selectedDecision ? (
                  <>
                    <MetadataRow label="Decision id" value={safeText(selectedDecision.decisionId)} />
                    <MetadataRow label="Request id" value={safeText(selectedDecision.toolRequestId)} />
                    <MetadataRow label="Decision status" value={<StatusBadge status="info">{safeText(selectedDecision.decisionStatus)}</StatusBadge>} />
                    <MetadataRow label="Policy outcome" value={safeText(selectedDecision.policyOutcomeSummary)} />
                    <MetadataRow label="Approval requirement" value={safeText(selectedDecision.approvalRequirementSummary)} />
                    <MetadataRow label="Reason" value={safeText(selectedDecision.safeReason)} />
                    <MetadataRow label="Decided" value={DateTimeDisplay.toLocalDisplay(selectedDecision.decidedUtc)} />
                    <MetadataRow label="Correlation" value={safeText(selectedDecision.correlationId ?? selectedRequest?.correlationId)} />
                    <MetadataRow label="Causation" value={safeText(selectedDecision.causationId)} />
                    <MetadataRow label="Summary" value={safeText(selectedDecision.safeSummary, 'No safe summary returned.')} />
                  </>
                ) : (
                  <p>No selected decision.</p>
                )}
              </div>
            </div>

            <div className="tool-gate-actions">
              <button type="button" className="command-button" onClick={copyRequestId} disabled={!selectedRequest}>
                Copy Request ID
              </button>
              <button type="button" className="command-button" onClick={copyDecisionId} disabled={!selectedDecision}>
                Copy Decision ID
              </button>
              <button type="button" className="command-button" onClick={() => setDetailMessage(requestDetailMessage(selectedRequest))} disabled={!selectedRequest}>
                Open Request
              </button>
              <button type="button" className="command-button" onClick={() => setDetailMessage(decisionDetailMessage(selectedDecision))} disabled={!selectedDecision}>
                Open Decision
              </button>
            </div>
            {copyMessage ? <p className="state-muted" data-testid="tool-gates.copyStatus">{copyMessage}</p> : null}
            <p className="state-muted" data-testid="tool-gates.detailStatus">{detailMessage}</p>
          </Surface>

          <Surface testId="tool-gates.related">
            <div className="section-heading">
              <p className="eyebrow">Related evidence</p>
              <h3>Read-only links</h3>
            </div>
            <div className="tool-gate-actions tool-gate-actions--stack">
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openTraceMessage(selectedRequest, selectedDecision))}>
                Open Trace
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openTimelineMessage(selectedRequest, selectedDecision))}>
                Open Timeline
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openApplyPreviewMessage(selectedRequest, selectedDecision))}>
                Open Apply Preview
              </button>
            </div>
            <p className="state-muted" data-testid="tool-gates.relatedStatus">{relatedMessage}</p>
            <p className="tool-gate-boundary" data-testid="tool-gates.boundaryFooter">
              This UI cannot approve, reject, execute tools, reopen gates, satisfy policy, transition workflow, apply source, or clean up data.
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
  name: keyof ToolGateFilters;
  value: string;
  onChange: (updater: (current: ToolGateFilters) => ToolGateFilters) => void;
}) {
  const id = `tool-gates.${name}`;
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

function IssueList({ issues, testId }: { issues: ToolGateIssue[]; testId: string }) {
  return (
    <div className="state-panel" data-testid={testId}>
      <h3>Validation error</h3>
      {issues.length === 0 ? (
        <p>The request was rejected by tool request or gate decision validation.</p>
      ) : (
        <ul className="detail-list">
          {issues.map((issue, index) => (
            <li key={`${issue.code ?? 'issue'}-${issue.field ?? 'field'}-${index}`}>
              {safeText(issue.field, 'query')}: {safeText(issue.message, 'Invalid tool gate request.')}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function coerceToolRequest(input: unknown): ToolRequestListItem | null {
  const record = pickRecord(input, ['toolRequest', 'request', 'detail', 'record']);
  const toolRequestId = coerceString(record, ['toolRequestId', 'requestId', 'id']);
  if (!toolRequestId) {
    return null;
  }

  return {
    toolRequestId,
    projectReferenceId: coerceString(record, ['projectReferenceId', 'projectId']),
    workflowRunId: coerceString(record, ['workflowRunId', 'runId']),
    workflowStepId: coerceString(record, ['workflowStepId', 'stepId']),
    correlationId: coerceString(record, ['correlationId']),
    requestedToolName: coerceString(record, ['requestedToolName', 'requestedTool', 'toolName', 'toolKind'], 'Tool unavailable'),
    requestedCapability: coerceString(record, ['requestedCapability', 'capability']),
    requestedOperation: coerceString(record, ['requestedOperation', 'operation', 'requestKind']),
    requestStatus: coerceString(record, ['requestStatus', 'status'], 'Recorded'),
    sourceComponent: coerceString(record, ['sourceComponent', 'requester', 'actorKind']),
    createdUtc: coerceString(record, ['createdUtc', 'recordedUtc']),
    subjectReference: coerceString(record, ['subjectReference', 'subjectReferenceId']),
    safeSummary: coerceString(record, ['safeSummary', 'summary', 'reason'], 'No safe summary returned.')
  };
}

function coerceToolDecision(input: unknown): ToolGateDecisionListItem | null {
  const record = pickRecord(input, ['gateDecision', 'decision', 'detail', 'record']);
  const decisionId = coerceString(record, ['decisionId', 'gateDecisionId', 'id']);
  if (!decisionId) {
    return null;
  }

  return {
    decisionId,
    toolRequestId: coerceString(record, ['toolRequestId', 'requestId'], 'Unavailable'),
    decisionStatus: coerceString(record, ['decisionStatus', 'status', 'gateStatus'], 'Recorded'),
    policyOutcomeSummary: coerceString(record, ['policyOutcomeSummary', 'policySummary'], 'Policy evidence is not policy satisfaction.'),
    approvalRequirementSummary: coerceString(record, ['approvalRequirementSummary', 'approvalSummary'], 'Approval requirement is not approval.'),
    safeReason: coerceString(record, ['safeReason', 'safeBlockReason', 'safeDenialReason', 'reason'], 'No safe reason returned.'),
    decidedUtc: coerceString(record, ['decidedUtc', 'createdUtc', 'recordedUtc']),
    correlationId: coerceString(record, ['correlationId']),
    causationId: coerceString(record, ['causationId']),
    subjectReference: coerceString(record, ['subjectReference', 'subjectReferenceId']),
    safeSummary: coerceString(record, ['safeSummary', 'summary'], 'No safe summary returned.')
  };
}

function pickRecord(input: unknown, keys: string[]) {
  const root = asRecord(input);
  const data = root.data && typeof root.data === 'object' && !Array.isArray(root.data) ? asRecord(root.data) : root;

  for (const key of keys) {
    const nested = asRecord(data[key]);
    if (nested) {
      return nested;
    }
  }

  return data;
}

function coerceString(record: Record<string, unknown>, keys: string[], fallback = '') {
  for (const key of keys) {
    const value = record[key];
    if (value !== undefined && value !== null) {
      return safeText(value as string | number, fallback);
    }
  }

  return fallback;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

function extractEnvelopeIssues(envelope: ToolGateApiEnvelope<unknown>) {
  return [...(envelope.errors ?? []), ...((asRecord(envelope.data).issues as ToolGateIssue[] | undefined) ?? [])];
}

function extractIssues(error: unknown): ToolGateIssue[] {
  if (!(error instanceof IronDevApiError)) {
    return [];
  }

  const body = error.body as Partial<ToolGateApiEnvelope<unknown>> | undefined;
  return [...(body?.errors ?? []), ...((asRecord(body?.data).issues as ToolGateIssue[] | undefined) ?? [])];
}

function safeText(value: string | number | null | undefined, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = text.toLowerCase().replace(/[^a-z0-9 -]/g, '');
  return unsafeToolGateTextMarkers.some((marker) => normalized.includes(marker)) ? redactedToolGateText : text;
}

function requestDetailMessage(request: ToolRequestListItem | null) {
  return request ? `Opened read-only request ${safeText(request.toolRequestId)}.` : 'Select a request before opening detail.';
}

function decisionDetailMessage(decision: ToolGateDecisionListItem | null) {
  return decision ? `Opened read-only decision ${safeText(decision.decisionId)}.` : 'Select a decision before opening detail.';
}

function openTraceMessage(request: ToolRequestListItem | null, decision: ToolGateDecisionListItem | null) {
  const correlation = decision?.correlationId ?? request?.correlationId;
  return correlation
    ? `/api/v1/governance/traces/by-correlation/${safeText(correlation)}`
    : 'Select evidence with a correlation reference before opening trace.';
}

function openTimelineMessage(request: ToolRequestListItem | null, decision: ToolGateDecisionListItem | null) {
  const correlation = decision?.correlationId ?? request?.correlationId;
  return correlation
    ? `/governance/timeline?correlationId=${safeText(correlation)}`
    : 'Select evidence with a correlation reference before opening timeline.';
}

function openApplyPreviewMessage(request: ToolRequestListItem | null, decision: ToolGateDecisionListItem | null) {
  const subject = decision?.subjectReference ?? request?.subjectReference;
  return subject
    ? `/apply/preview?subjectReferenceId=${safeText(subject)}`
    : 'No safe apply-preview reference was returned for the selected evidence.';
}
