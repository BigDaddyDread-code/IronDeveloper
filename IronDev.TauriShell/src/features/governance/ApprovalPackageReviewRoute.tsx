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
import type {
  ApprovalPackageEvidenceReferenceView,
  ApprovalPackageLoadStatus,
  ApprovalPackageReviewDetail,
  ApprovalPackageReviewFilters,
  ApprovalPackageReviewItem
} from './ApprovalPackageReviewTypes';
import { fromTraceSummary } from './ApprovalPackageReviewTypes';

interface ApprovalPackageReviewRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const emptyFilters: ApprovalPackageReviewFilters = {
  projectReferenceId: '',
  workflowRunId: '',
  workflowStepId: '',
  approvalPackageId: '',
  correlationId: '',
  approvalScope: '',
  packageStatus: '',
  sourceComponent: '',
  fromUtc: '',
  toUtc: '',
  take: '50'
};

const boundaryWarnings = [
  'Read-only view',
  'Approval package is not accepted approval',
  'Approval package review is not approval',
  'Requested decision is not decision made',
  'Policy evidence is not policy satisfaction'
];

const redactedApprovalPackageText = '[redacted approval package text]';

const unsafeApprovalPackageTextMarkers = [
  'payload' + 'json',
  'approval' + 'payloadjson',
  'approval payload json',
  'approval' + 'notesraw',
  'approval notes raw',
  'raw' + 'approvaltext',
  'raw approval text',
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

export function ApprovalPackageReviewRoute({ route, onRouteReady }: ApprovalPackageReviewRouteProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<ApprovalPackageReviewFilters>(emptyFilters);
  const [status, setStatus] = useState<ApprovalPackageLoadStatus>('idle');
  const [message, setMessage] = useState('Set project, package, or correlation filters, then search approval package evidence.');
  const [packages, setPackages] = useState<ApprovalPackageReviewItem[]>([]);
  const [selectedPackageId, setSelectedPackageId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ApprovalPackageReviewDetail | null>(null);
  const [issues, setIssues] = useState<GovernanceTraceIssue[]>([]);
  const [detailMessage, setDetailMessage] = useState('Open a package to inspect safe approval evidence references.');
  const [relatedMessage, setRelatedMessage] = useState('Related links stay read-only and do not continue workflow.');
  const [copyMessage, setCopyMessage] = useState('');

  const selectedPackage = useMemo(
    () => packages.find((item) => item.approvalPackageId === selectedPackageId) ?? packages[0] ?? null,
    [packages, selectedPackageId]
  );

  const canQuery = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const search = useCallback(async () => {
    if (!canQuery) {
      setStatus('error');
      setMessage('Connect IronDev.Api and sign in before loading approval package evidence.');
      setPackages([]);
      setIssues([]);
      return;
    }

    if (!hasSearchBasis(filters)) {
      setStatus('validation');
      setMessage('Approval package review needs a project reference, approval package id, workflow run, or correlation id.');
      setIssues([
        {
          code: 'missing_approval_package_filter',
          field: 'approvalPackageId',
          message: 'Provide a project reference, approval package id, workflow run, or correlation id for read-only inspection.'
        }
      ]);
      setPackages([]);
      setDetail(null);
      return;
    }

    setStatus('loading');
    setMessage('Loading approval package evidence...');
    setIssues([]);

    const controller = new AbortController();

    try {
      const response = await session.client.searchGovernanceTraces(toTraceQuery(filters), controller.signal);
      const responseIssues = [...(response.errors ?? []), ...(response.data?.issues ?? [])];
      const nextPackages = sanitizeTraceSummaries(response.data?.traces ?? []).map((trace) => fromTraceSummary(trace, filters, safeText));

      setIssues(responseIssues);
      setPackages(nextPackages);
      setSelectedPackageId(nextPackages[0]?.approvalPackageId ?? null);
      setDetail(null);
      setDetailMessage('Open a package to inspect safe approval evidence references.');
      setStatus(nextPackages.length === 0 ? 'empty' : 'loaded');
      setMessage(nextPackages.length === 0 ? 'No approval package evidence matched those filters.' : `Loaded ${nextPackages.length} approval package item(s).`);
    } catch (error) {
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setPackages([]);
      setSelectedPackageId(null);
      setDetail(null);
      setStatus(nextIssues.length > 0 ? 'validation' : 'error');
      setMessage(nextIssues.length > 0 ? 'Approval package evidence request was rejected by validation.' : 'Approval package evidence request failed.');
    } finally {
      controller.abort();
    }
  }, [canQuery, filters, session.client]);

  const openPackage = useCallback(async () => {
    if (!selectedPackage) {
      setDetail(null);
      setDetailMessage('Select an approval package before opening detail.');
      return;
    }

    setDetailMessage('Loading safe approval package detail...');
    const controller = new AbortController();

    try {
      const response = await session.client.getGovernanceTrace(selectedPackage.traceId, controller.signal);
      const trace = sanitizeTraceDetail(response.data?.trace ?? null);
      setDetail(buildDetail(selectedPackage, trace));
      setDetailMessage(trace ? 'Approval package detail loaded with safe evidence summaries only.' : 'Approval package detail was not returned.');
    } catch (error) {
      setDetail(null);
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setDetailMessage(nextIssues.length > 0 ? 'Approval package detail request was rejected by validation.' : 'Approval package detail request failed.');
    } finally {
      controller.abort();
    }
  }, [selectedPackage, session.client]);

  const clearFilters = useCallback(() => {
    setFilters(emptyFilters);
    setIssues([]);
    setPackages([]);
    setSelectedPackageId(null);
    setDetail(null);
    setMessage('Filters cleared. Review remains read-only and does not sign anything.');
  }, []);

  const copyPackageId = useCallback(() => {
    if (!selectedPackage) {
      setCopyMessage('Select an approval package before copying an id.');
      return;
    }

    void navigator.clipboard?.writeText(selectedPackage.approvalPackageId).catch(() => undefined);
    setCopyMessage('Package id copied for inspection only. Copy package id is not approval.');
  }, [selectedPackage]);

  const copyCorrelationId = useCallback(() => {
    if (!selectedPackage?.correlationId) {
      setCopyMessage('Select a package with a correlation reference before copying correlation.');
      return;
    }

    void navigator.clipboard?.writeText(selectedPackage.correlationId).catch(() => undefined);
    setCopyMessage('Correlation id copied for inspection only. Copy correlation id is not workflow continuation.');
  }, [selectedPackage]);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: `workspace.${route.id}.approvalPackages.refresh`,
        label: status === 'loading' ? 'Refreshing approval packages' : 'Refresh approval packages',
        intent: 'secondary',
        onExecute: () => void search(),
        disabled: status === 'loading',
        busy: status === 'loading',
        testId: 'approval-packages.command.refresh',
        disabledReason: canQuery ? undefined : 'Connect IronDev.Api and sign in before refreshing approval package evidence.'
      }
    ],
    [canQuery, route.id, search, status]
  );

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: canQuery ? null : 'Approval package review requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: `${packages.length} package(s)`, testId: 'approval-packages.chip.count' },
        { label: 'Read-only', testId: 'approval-packages.chip.readonly' }
      ],
      blockReasonTestId: canQuery ? undefined : 'approval-packages.blockedReason'
    }),
    [canQuery, commands, packages.length]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  return (
    <main className="approval-package-workspace" data-testid="approval-packages.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Approval package review</p>
            <h2>Approval Package Review</h2>
            <p>
              Inspect approval package evidence through existing read-only governance trace APIs. This page is the review table,
              not the signature line.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="approval-packages.readonlyBanner">
            {boundaryWarnings.map((warning) => (
              <span key={warning}>{warning}</span>
            ))}
          </div>
        </div>

        <Surface testId="approval-packages.filters" className="approval-package-filters">
          <div className="section-heading">
            <p className="eyebrow">Filters</p>
            <h3>Read-only lookup</h3>
          </div>
          <div className="approval-package-filter-grid">
            <FilterInput label="Project reference" name="projectReferenceId" value={filters.projectReferenceId} onChange={setFilters} />
            <FilterInput label="Workflow run" name="workflowRunId" value={filters.workflowRunId} onChange={setFilters} />
            <FilterInput label="Workflow step" name="workflowStepId" value={filters.workflowStepId} onChange={setFilters} />
            <FilterInput label="Approval package id" name="approvalPackageId" value={filters.approvalPackageId} onChange={setFilters} />
            <FilterInput label="Correlation" name="correlationId" value={filters.correlationId} onChange={setFilters} />
            <FilterInput label="Approval scope" name="approvalScope" value={filters.approvalScope} onChange={setFilters} />
            <FilterInput label="Package status" name="packageStatus" value={filters.packageStatus} onChange={setFilters} />
            <FilterInput label="Source component" name="sourceComponent" value={filters.sourceComponent} onChange={setFilters} />
            <FilterInput label="From UTC" name="fromUtc" value={filters.fromUtc} onChange={setFilters} />
            <FilterInput label="To UTC" name="toUtc" value={filters.toUtc} onChange={setFilters} />
            <FilterInput label="Take" name="take" value={filters.take} onChange={setFilters} />
          </div>
          <div className="approval-package-actions">
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

        <div className="approval-package-layout">
          <Surface testId="approval-packages.list">
            <div className="section-heading">
              <p className="eyebrow">Packages</p>
              <h3>Review queue evidence</h3>
            </div>
            <p className="state-muted" data-testid="approval-packages.status">{message}</p>
            {status === 'validation' ? (
              <IssueList issues={issues} testId="approval-packages.validationError" />
            ) : status === 'empty' || packages.length === 0 ? (
              <EmptyState title="No approval package evidence" body={message} />
            ) : (
              <div className="approval-package-card-list" data-testid="approval-packages.items">
                {packages.map((item) => (
                  <button
                    key={`${item.approvalPackageId}-${item.traceId}`}
                    type="button"
                    className={`approval-package-card ${item.approvalPackageId === selectedPackageId ? 'approval-package-card--selected' : ''}`}
                    data-testid="approval-packages.item"
                    onClick={() => {
                      setSelectedPackageId(item.approvalPackageId);
                      setDetail(null);
                      setDetailMessage('Open a package to inspect safe approval evidence references.');
                    }}
                  >
                    <span className="approval-package-card__kind">{safeText(item.packageStatus, 'review')}</span>
                    <strong>{safeText(item.approvalPackageId)}</strong>
                    <span>{safeText(item.safeSummary, 'No safe summary returned.')}</span>
                    <span>{DateTimeDisplay.toLocalDisplay(item.createdUtc)}</span>
                  </button>
                ))}
              </div>
            )}
          </Surface>

          <Surface testId="approval-packages.detail">
            <div className="section-heading">
              <p className="eyebrow">Selected package</p>
              <h3>{selectedPackage?.approvalPackageId ?? 'No package selected'}</h3>
            </div>
            {selectedPackage ? (
              <div className="approval-package-detail-grid" data-testid="approval-packages.safeDetail">
                <div className="workflow-section">
                  <h3>Package summary</h3>
                  <MetadataRow label="Package id" value={safeText(selectedPackage.approvalPackageId)} />
                  <MetadataRow label="Requested decision" value={safeText(selectedPackage.requestedDecision)} />
                  <MetadataRow label="Approval scope" value={safeText(selectedPackage.approvalScope)} />
                  <MetadataRow label="Package status" value={<StatusBadge status="info">{safeText(selectedPackage.packageStatus)}</StatusBadge>} />
                  <MetadataRow label="Workflow run" value={safeText(selectedPackage.workflowRunId)} />
                  <MetadataRow label="Workflow step" value={safeText(selectedPackage.workflowStepId)} />
                  <MetadataRow label="Correlation" value={safeText(selectedPackage.correlationId)} />
                  <MetadataRow label="Source component" value={safeText(selectedPackage.sourceComponent)} />
                  <MetadataRow label="Created" value={DateTimeDisplay.toLocalDisplay(selectedPackage.createdUtc)} />
                  <MetadataRow label="Summary" value={safeText(selectedPackage.safeSummary, 'No safe summary returned.')} />
                </div>
                <div className="workflow-section">
                  <h3>Evidence references</h3>
                  {detail?.evidenceReferences.length ? (
                    <ul className="detail-list" data-testid="approval-packages.evidenceRefs">
                      {detail.evidenceReferences.map((reference, index) => (
                        <li key={`${reference.referenceKind}-${reference.referenceId}-${index}`}>
                          <strong>{safeText(reference.referenceKind, 'reference')}</strong> {safeText(reference.referenceId)} -{' '}
                          {safeText(reference.safeSummary, 'No safe summary returned.')}
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p>No safe evidence references loaded yet. Open the package detail to inspect references.</p>
                  )}
                </div>
              </div>
            ) : (
              <EmptyState title="Select a package" body="Approval package review is not approval." />
            )}

            <div className="approval-package-actions">
              <button type="button" className="command-button" onClick={copyPackageId} disabled={!selectedPackage}>
                Copy Package ID
              </button>
              <button type="button" className="command-button" onClick={copyCorrelationId} disabled={!selectedPackage?.correlationId}>
                Copy Correlation ID
              </button>
              <button type="button" className="command-button" onClick={() => void openPackage()} disabled={!selectedPackage || status === 'loading'}>
                Open Package
              </button>
            </div>
            {copyMessage ? <p className="state-muted" data-testid="approval-packages.copyStatus">{copyMessage}</p> : null}
            <p className="state-muted" data-testid="approval-packages.detailStatus">{detailMessage}</p>
            <DetailWarnings detail={detail} />
          </Surface>

          <Surface testId="approval-packages.related">
            <div className="section-heading">
              <p className="eyebrow">Related evidence</p>
              <h3>Read-only navigation</h3>
            </div>
            <div className="approval-package-actions approval-package-actions--stack">
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openTraceMessage(selectedPackage))}>
                Open Trace
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openTimelineMessage(selectedPackage))}>
                Open Timeline
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openCorrelationReportMessage(selectedPackage))}>
                Open Correlation Report
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openToolGateLedgerMessage(selectedPackage))}>
                Open Tool Gate Ledger
              </button>
            </div>
            <p className="state-muted" data-testid="approval-packages.relatedStatus">{relatedMessage}</p>
            <p className="approval-package-boundary" data-testid="approval-packages.boundaryFooter">
              This UI cannot approve, reject, accept approvals, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software.
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
  name: keyof ApprovalPackageReviewFilters;
  value: string;
  onChange: (updater: (current: ApprovalPackageReviewFilters) => ApprovalPackageReviewFilters) => void;
}) {
  const id = `approval-packages.${name}`;
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
        <p>The request was rejected by approval package review validation.</p>
      ) : (
        <ul className="detail-list">
          {issues.map((issue, index) => (
            <li key={`${issue.code ?? 'issue'}-${issue.field ?? 'field'}-${index}`}>
              {safeText(issue.field, 'query')}: {safeText(issue.message, 'Invalid approval package review request.')}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function DetailWarnings({ detail }: { detail: ApprovalPackageReviewDetail | null }) {
  if (!detail) {
    return null;
  }

  return (
    <div className="approval-package-detail-grid" data-testid="approval-packages.boundaryWarnings">
      <div className="workflow-section">
        <h3>Boundary warnings</h3>
        <ul className="detail-list">
          {detail.boundaryWarnings.map((warning, index) => (
            <li key={`${warning}-${index}`}>{safeText(warning)}</li>
          ))}
        </ul>
      </div>
      <div className="workflow-section">
        <h3>Safe timeline summaries</h3>
        {detail.safeTimelineSummaries.length === 0 ? (
          <p>No safe timeline summaries were returned.</p>
        ) : (
          <ul className="detail-list">
            {detail.safeTimelineSummaries.map((summary, index) => (
              <li key={`${summary}-${index}`}>{safeText(summary)}</li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function hasSearchBasis(filters: ApprovalPackageReviewFilters) {
  return Boolean(
    filters.projectReferenceId.trim() ||
      filters.approvalPackageId.trim() ||
      filters.workflowRunId.trim() ||
      filters.workflowStepId.trim() ||
      filters.correlationId.trim()
  );
}

function toTraceQuery(filters: ApprovalPackageReviewFilters): GovernanceTraceQuery {
  const take = Number.parseInt(filters.take, 10);
  return {
    projectReferenceId: filters.projectReferenceId.trim(),
    workflowRunId: filters.workflowRunId.trim(),
    workflowStepId: filters.workflowStepId.trim(),
    correlationId: filters.correlationId.trim(),
    subjectReferenceId: filters.approvalPackageId.trim(),
    eventKind: 'human_approval_package',
    sourceComponent: filters.sourceComponent.trim(),
    fromUtc: filters.fromUtc.trim(),
    toUtc: filters.toUtc.trim(),
    take: Number.isFinite(take) ? take : 50
  };
}

function buildDetail(item: ApprovalPackageReviewItem, trace: GovernanceTraceDetail | null): ApprovalPackageReviewDetail {
  const evidenceReferences: ApprovalPackageEvidenceReferenceView[] = (trace?.relatedReferences ?? []).map((reference) => ({
    referenceKind: safeText(reference.referenceKind, 'reference'),
    referenceId: safeText(reference.referenceId),
    safeSummary: safeText(reference.safeSummary, 'No safe summary returned.')
  }));

  const safeTimelineSummaries = (trace?.timeline ?? []).map((timeline) => safeText(timeline.safeSummary, 'No safe timeline summary returned.'));
  const warnings = trace?.boundaryWarnings?.length
    ? trace.boundaryWarnings.map((warning) => safeText(warning))
    : ['Approval package detail is evidence only.', 'Human review remains required.', 'No accepted approval record is created.'];

  return {
    item,
    evidenceReferences,
    boundaryWarnings: warnings,
    safeTimelineSummaries,
    trace
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

  const normalized = text.toLowerCase().replace(/[^a-z0-9 -]/g, '');
  return unsafeApprovalPackageTextMarkers.some((marker) => normalized.includes(marker)) ? redactedApprovalPackageText : text;
}

function openTraceMessage(item: ApprovalPackageReviewItem | null) {
  return item?.traceId ? `/api/v1/governance/traces/${safeText(item.traceId)}` : 'Select an approval package before opening trace.';
}

function openTimelineMessage(item: ApprovalPackageReviewItem | null) {
  return item?.correlationId ? `/governance/timeline?correlationId=${safeText(item.correlationId)}` : 'Select a package with correlation before opening timeline.';
}

function openCorrelationReportMessage(item: ApprovalPackageReviewItem | null) {
  return item?.correlationId
    ? `/api/v1/governance/correlation-reports/approval-gate-dogfood?correlationId=${safeText(item.correlationId)}`
    : 'Select a package with correlation before opening correlation report.';
}

function openToolGateLedgerMessage(item: ApprovalPackageReviewItem | null) {
  return item?.workflowRunId ? `/governance/tool-gates?workflowRunId=${safeText(item.workflowRunId)}` : 'Select a package with workflow run before opening tool gate ledger.';
}
