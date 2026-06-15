import { useCallback, useEffect, useMemo, useState } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  DogfoodLoopApiEnvelope,
  DogfoodLoopIssue,
  DogfoodReceiptDetailData,
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
  DogfoodReceiptLoadStatus,
  DogfoodReceiptReferenceView,
  DogfoodReceiptViewerDetail,
  DogfoodReceiptViewerFilters,
  DogfoodReceiptViewerItem
} from './DogfoodReceiptViewerTypes';
import { itemFromReceiptData, itemFromTraceSummary, referencesFromReceipt } from './DogfoodReceiptViewerTypes';

interface DogfoodReceiptViewerRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const emptyFilters: DogfoodReceiptViewerFilters = {
  projectReferenceId: '',
  dogfoodLoopId: '',
  dogfoodReceiptId: '',
  workflowRunId: '',
  workflowStepId: '',
  correlationId: '',
  sourceComponent: '',
  fromUtc: '',
  toUtc: '',
  take: '50'
};

const boundaryWarnings = [
  'Read-only view',
  'Dogfood receipt is not release approval',
  'Dogfood pass is not release readiness',
  'Dogfood evidence is not policy satisfaction',
  'Receipt viewer is not dogfood execution'
];

const redactedDogfoodReceiptText = '[redacted dogfood receipt text]';

const unsafeDogfoodReceiptTextMarkers = [
  'payload' + 'json',
  'dogfood' + 'payloadjson',
  'dogfood payload json',
  'dogfood output json',
  'validation output json',
  'raw' + 'dogfoodnotes',
  'raw dogfood notes',
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

export function DogfoodReceiptViewerRoute({ route, onRouteReady }: DogfoodReceiptViewerRouteProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<DogfoodReceiptViewerFilters>(emptyFilters);
  const [status, setStatus] = useState<DogfoodReceiptLoadStatus>('idle');
  const [message, setMessage] = useState('Set project, receipt, workflow, or correlation filters, then search dogfood receipt evidence.');
  const [receipts, setReceipts] = useState<DogfoodReceiptViewerItem[]>([]);
  const [selectedReceiptId, setSelectedReceiptId] = useState<string | null>(null);
  const [detail, setDetail] = useState<DogfoodReceiptViewerDetail | null>(null);
  const [issues, setIssues] = useState<Array<DogfoodLoopIssue | GovernanceTraceIssue>>([]);
  const [detailMessage, setDetailMessage] = useState('Open a receipt to inspect safe dogfood evidence references.');
  const [relatedMessage, setRelatedMessage] = useState('Related links stay read-only and do not continue workflow.');
  const [copyMessage, setCopyMessage] = useState('');

  const selectedReceipt = useMemo(
    () => receipts.find((item) => item.dogfoodReceiptId === selectedReceiptId) ?? receipts[0] ?? null,
    [receipts, selectedReceiptId]
  );

  const canQuery = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const search = useCallback(async () => {
    if (!canQuery) {
      setStatus('error');
      setMessage('Connect IronDev.Api and sign in before loading dogfood receipt evidence.');
      setReceipts([]);
      setIssues([]);
      return;
    }

    if (!hasSearchBasis(filters)) {
      setStatus('validation');
      setMessage('Dogfood receipt viewer needs a project reference, dogfood receipt id, workflow run, or correlation id.');
      setIssues([
        {
          code: 'missing_dogfood_receipt_filter',
          field: 'dogfoodReceiptId',
          message: 'Provide a project reference, dogfood receipt id, workflow run, or correlation id for read-only inspection.'
        }
      ]);
      setReceipts([]);
      setDetail(null);
      return;
    }

    setStatus('loading');
    setMessage('Loading dogfood receipt evidence...');
    setIssues([]);

    const controller = new AbortController();

    try {
      if (canLoadReceiptDirectly(filters)) {
        const response = await session.client.getDogfoodLoopReceipt(receiptLookupId(filters), filters.projectReferenceId.trim(), controller.signal);
        const responseIssues = response.errors ?? [];
        const data = sanitizeReceiptData(response.data ?? null);
        const nextReceipts = data ? [itemFromReceiptData(data, filters, safeText)] : [];

        setIssues(responseIssues);
        setReceipts(nextReceipts);
        setSelectedReceiptId(nextReceipts[0]?.dogfoodReceiptId ?? null);
        setDetail(data && nextReceipts[0] ? buildDetailFromReceipt(nextReceipts[0], data, response.warnings ?? []) : null);
        setDetailMessage(data ? 'Dogfood receipt detail loaded with safe evidence summaries only.' : 'Dogfood receipt detail was not returned.');
        setStatus(nextReceipts.length === 0 ? 'empty' : 'loaded');
        setMessage(nextReceipts.length === 0 ? 'No dogfood receipt matched those filters.' : `Loaded ${nextReceipts.length} dogfood receipt item(s).`);
        return;
      }

      const response = await session.client.searchGovernanceTraces(toTraceQuery(filters), controller.signal);
      const responseIssues = [...(response.errors ?? []), ...(response.data?.issues ?? [])];
      const nextReceipts = sanitizeTraceSummaries(response.data?.traces ?? []).map((trace) => itemFromTraceSummary(trace, filters, safeText));

      setIssues(responseIssues);
      setReceipts(nextReceipts);
      setSelectedReceiptId(nextReceipts[0]?.dogfoodReceiptId ?? null);
      setDetail(null);
      setDetailMessage('Open a receipt to inspect safe dogfood evidence references.');
      setStatus(nextReceipts.length === 0 ? 'empty' : 'loaded');
      setMessage(nextReceipts.length === 0 ? 'No dogfood receipt evidence matched those filters.' : `Loaded ${nextReceipts.length} dogfood receipt item(s).`);
    } catch (error) {
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setReceipts([]);
      setSelectedReceiptId(null);
      setDetail(null);
      setStatus(nextIssues.length > 0 ? 'validation' : 'error');
      setMessage(nextIssues.length > 0 ? 'Dogfood receipt request was rejected by validation.' : 'Dogfood receipt request failed.');
    } finally {
      controller.abort();
    }
  }, [canQuery, filters, session.client]);

  const openReceipt = useCallback(async () => {
    if (!selectedReceipt) {
      setDetail(null);
      setDetailMessage('Select a dogfood receipt before opening detail.');
      return;
    }

    setDetailMessage('Loading safe dogfood receipt detail...');
    const controller = new AbortController();

    try {
      if (selectedReceipt.dogfoodLoopId && selectedReceipt.projectReferenceId) {
        const response = await session.client.getDogfoodLoopReceipt(selectedReceipt.dogfoodLoopId, selectedReceipt.projectReferenceId, controller.signal);
        const data = sanitizeReceiptData(response.data ?? null);
        setDetail(data ? buildDetailFromReceipt(itemFromReceiptData(data, filters, safeText), data, response.warnings ?? []) : null);
        setDetailMessage(data ? 'Dogfood receipt detail loaded with safe evidence summaries only.' : 'Dogfood receipt detail was not returned.');
        return;
      }

      if (selectedReceipt.traceId) {
        const response = await session.client.getGovernanceTrace(selectedReceipt.traceId, controller.signal);
        const trace = sanitizeTraceDetail(response.data?.trace ?? null);
        setDetail(buildDetailFromTrace(selectedReceipt, trace, response.warnings ?? []));
        setDetailMessage(trace ? 'Dogfood receipt trace detail loaded with safe summaries only.' : 'Dogfood receipt trace detail was not returned.');
        return;
      }

      setDetail(null);
      setDetailMessage('Selected receipt has no readable dogfood loop or trace reference.');
    } catch (error) {
      setDetail(null);
      const nextIssues = extractIssues(error);
      setIssues(nextIssues);
      setDetailMessage(nextIssues.length > 0 ? 'Dogfood receipt detail request was rejected by validation.' : 'Dogfood receipt detail request failed.');
    } finally {
      controller.abort();
    }
  }, [filters, selectedReceipt, session.client]);

  const clearFilters = useCallback(() => {
    setFilters(emptyFilters);
    setIssues([]);
    setReceipts([]);
    setSelectedReceiptId(null);
    setDetail(null);
    setMessage('Filters cleared. Receipt viewing remains read-only and does not taste the dogfood.');
  }, []);

  const copyReceiptId = useCallback(() => {
    if (!selectedReceipt) {
      setCopyMessage('Select a dogfood receipt before copying an id.');
      return;
    }

    void navigator.clipboard?.writeText(selectedReceipt.dogfoodReceiptId).catch(() => undefined);
    setCopyMessage('Receipt id copied for inspection only. Copy receipt id is not release approval.');
  }, [selectedReceipt]);

  const copyCorrelationId = useCallback(() => {
    if (!selectedReceipt?.correlationId) {
      setCopyMessage('Select a receipt with a correlation reference before copying correlation.');
      return;
    }

    void navigator.clipboard?.writeText(selectedReceipt.correlationId).catch(() => undefined);
    setCopyMessage('Correlation id copied for inspection only. Copy correlation id is not workflow continuation.');
  }, [selectedReceipt]);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: `workspace.${route.id}.dogfoodReceipts.refresh`,
        label: status === 'loading' ? 'Refreshing dogfood receipts' : 'Refresh dogfood receipts',
        intent: 'secondary',
        onExecute: () => void search(),
        disabled: status === 'loading',
        busy: status === 'loading',
        testId: 'dogfood-receipts.command.refresh',
        disabledReason: canQuery ? undefined : 'Connect IronDev.Api and sign in before refreshing dogfood receipt evidence.'
      }
    ],
    [canQuery, route.id, search, status]
  );

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: canQuery ? null : 'Dogfood receipt viewer requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: `${receipts.length} receipt(s)`, testId: 'dogfood-receipts.chip.count' },
        { label: 'Read-only', testId: 'dogfood-receipts.chip.readonly' }
      ],
      blockReasonTestId: canQuery ? undefined : 'dogfood-receipts.blockedReason'
    }),
    [canQuery, commands, receipts.length]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  return (
    <main className="dogfood-receipt-workspace" data-testid="dogfood-receipts.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Dogfood receipt viewer</p>
            <h2>Dogfood Receipt Viewer</h2>
            <p>
              Inspect dogfood receipt evidence through existing read-only APIs. This page is the tasting note, not the kitchen.
            </p>
          </div>
          <div className="dogfood-receipt-banner" data-testid="dogfood-receipts.readonlyBanner">
            {boundaryWarnings.map((warning) => (
              <span key={warning}>{warning}</span>
            ))}
          </div>
        </div>

        <Surface testId="dogfood-receipts.filters" className="dogfood-receipt-filters">
          <div className="section-heading">
            <p className="eyebrow">Filters</p>
            <h3>Read-only lookup</h3>
          </div>
          <div className="dogfood-receipt-filter-grid">
            <FilterInput label="Project reference" name="projectReferenceId" value={filters.projectReferenceId} onChange={setFilters} />
            <FilterInput label="Dogfood loop id" name="dogfoodLoopId" value={filters.dogfoodLoopId} onChange={setFilters} />
            <FilterInput label="Dogfood receipt id" name="dogfoodReceiptId" value={filters.dogfoodReceiptId} onChange={setFilters} />
            <FilterInput label="Workflow run" name="workflowRunId" value={filters.workflowRunId} onChange={setFilters} />
            <FilterInput label="Workflow step" name="workflowStepId" value={filters.workflowStepId} onChange={setFilters} />
            <FilterInput label="Correlation" name="correlationId" value={filters.correlationId} onChange={setFilters} />
            <FilterInput label="Source component" name="sourceComponent" value={filters.sourceComponent} onChange={setFilters} />
            <FilterInput label="From UTC" name="fromUtc" value={filters.fromUtc} onChange={setFilters} />
            <FilterInput label="To UTC" name="toUtc" value={filters.toUtc} onChange={setFilters} />
            <FilterInput label="Take" name="take" value={filters.take} onChange={setFilters} />
          </div>
          <div className="dogfood-receipt-actions">
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

        <div className="dogfood-receipt-layout">
          <Surface testId="dogfood-receipts.list">
            <div className="section-heading">
              <p className="eyebrow">Receipts</p>
              <h3>Dogfood evidence</h3>
            </div>
            <p className="state-muted" data-testid="dogfood-receipts.status">{message}</p>
            {status === 'validation' ? (
              <IssueList issues={issues} testId="dogfood-receipts.validationError" />
            ) : status === 'empty' || receipts.length === 0 ? (
              <EmptyState title="No dogfood receipt evidence" body={message} />
            ) : (
              <div className="dogfood-receipt-card-list" data-testid="dogfood-receipts.items">
                {receipts.map((item) => (
                  <button
                    key={`${item.dogfoodReceiptId}-${item.traceId || item.dogfoodLoopId}`}
                    type="button"
                    className={`dogfood-receipt-card ${item.dogfoodReceiptId === selectedReceiptId ? 'dogfood-receipt-card--selected' : ''}`}
                    data-testid="dogfood-receipts.item"
                    onClick={() => {
                      setSelectedReceiptId(item.dogfoodReceiptId);
                      setDetail(null);
                      setDetailMessage('Open a receipt to inspect safe dogfood evidence references.');
                    }}
                  >
                    <span className="dogfood-receipt-card__kind">{item.durable ? 'Durable evidence' : 'Evidence view'}</span>
                    <strong>{safeText(item.dogfoodReceiptId)}</strong>
                    <span>{safeText(item.safeSummary, 'No safe dogfood receipt summary returned.')}</span>
                    <span>{DateTimeDisplay.toLocalDisplay(item.createdUtc)}</span>
                  </button>
                ))}
              </div>
            )}
          </Surface>

          <Surface testId="dogfood-receipts.detail">
            <div className="section-heading">
              <p className="eyebrow">Selected receipt</p>
              <h3>{selectedReceipt?.dogfoodReceiptId ?? 'No receipt selected'}</h3>
            </div>
            {selectedReceipt ? (
              <div className="dogfood-receipt-detail-grid" data-testid="dogfood-receipts.safeDetail">
                <div className="workflow-section">
                  <h3>Receipt summary</h3>
                  <MetadataRow label="Receipt id" value={safeText(selectedReceipt.dogfoodReceiptId)} />
                  <MetadataRow label="Dogfood loop id" value={safeText(selectedReceipt.dogfoodLoopId)} />
                  <MetadataRow label="Evidence id" value={safeText(selectedReceipt.evidenceId)} />
                  <MetadataRow label="Durable" value={<StatusBadge status={selectedReceipt.durable ? 'info' : 'warning'}>{selectedReceipt.durable ? 'Durable' : 'Trace evidence'}</StatusBadge>} />
                  <MetadataRow label="Workflow run" value={safeText(selectedReceipt.workflowRunId)} />
                  <MetadataRow label="Workflow step" value={safeText(selectedReceipt.workflowStepId)} />
                  <MetadataRow label="Correlation" value={safeText(selectedReceipt.correlationId)} />
                  <MetadataRow label="Source component" value={safeText(selectedReceipt.sourceComponent)} />
                  <MetadataRow label="Created" value={DateTimeDisplay.toLocalDisplay(selectedReceipt.createdUtc)} />
                  <MetadataRow label="Summary" value={safeText(selectedReceipt.safeSummary, 'No safe summary returned.')} />
                </div>
                <div className="workflow-section">
                  <h3>Evidence references</h3>
                  {detail?.evidenceReferences.length ? (
                    <ReferenceList references={detail.evidenceReferences} testId="dogfood-receipts.evidenceRefs" />
                  ) : (
                    <p>No safe evidence references loaded yet. Open the receipt detail to inspect references.</p>
                  )}
                </div>
              </div>
            ) : (
              <EmptyState title="Select a dogfood receipt" body="Dogfood receipt is not release approval." />
            )}

            <div className="dogfood-receipt-actions">
              <button type="button" className="command-button" onClick={copyReceiptId} disabled={!selectedReceipt}>
                Copy Receipt ID
              </button>
              <button type="button" className="command-button" onClick={copyCorrelationId} disabled={!selectedReceipt?.correlationId}>
                Copy Correlation ID
              </button>
              <button type="button" className="command-button" onClick={() => void openReceipt()} disabled={!selectedReceipt || status === 'loading'}>
                Open Receipt
              </button>
            </div>
            {copyMessage ? <p className="state-muted" data-testid="dogfood-receipts.copyStatus">{copyMessage}</p> : null}
            <p className="state-muted" data-testid="dogfood-receipts.detailStatus">{detailMessage}</p>
            <DetailSections detail={detail} />
          </Surface>

          <Surface testId="dogfood-receipts.related">
            <div className="section-heading">
              <p className="eyebrow">Related evidence</p>
              <h3>Read-only navigation</h3>
            </div>
            <div className="dogfood-receipt-actions dogfood-receipt-actions--stack">
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openTraceMessage(selectedReceipt))}>
                Open Trace
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openTimelineMessage(selectedReceipt))}>
                Open Timeline
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openCorrelationReportMessage(selectedReceipt))}>
                Open Correlation Report
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openToolGateLedgerMessage(selectedReceipt))}>
                Open Tool Gate Ledger
              </button>
              <button type="button" className="command-button" onClick={() => setRelatedMessage(openApprovalPackageMessage(selectedReceipt))}>
                Open Approval Package
              </button>
            </div>
            <p className="state-muted" data-testid="dogfood-receipts.relatedStatus">{relatedMessage}</p>
            <p className="dogfood-receipt-boundary" data-testid="dogfood-receipts.boundaryFooter">
              This UI cannot create dogfood receipts, mark dogfood passed, approve release, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software.
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
  name: keyof DogfoodReceiptViewerFilters;
  value: string;
  onChange: (updater: (current: DogfoodReceiptViewerFilters) => DogfoodReceiptViewerFilters) => void;
}) {
  const id = `dogfood-receipts.${name}`;
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

function IssueList({ issues, testId }: { issues: Array<DogfoodLoopIssue | GovernanceTraceIssue>; testId: string }) {
  return (
    <div className="state-panel" data-testid={testId}>
      <h3>Validation error</h3>
      {issues.length === 0 ? (
        <p>The request was rejected by dogfood receipt viewer validation.</p>
      ) : (
        <ul className="detail-list">
          {issues.map((issue, index) => (
            <li key={`${issue.code ?? 'issue'}-${issue.field ?? 'field'}-${index}`}>
              {safeText(issue.field, 'query')}: {safeText(issue.message, 'Invalid dogfood receipt viewer request.')}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ReferenceList({ references, testId }: { references: DogfoodReceiptReferenceView[]; testId: string }) {
  return (
    <ul className="detail-list" data-testid={testId}>
      {references.map((reference, index) => (
        <li key={`${reference.referenceKind}-${reference.referenceId}-${index}`}>
          <strong>{safeText(reference.referenceKind, 'reference')}</strong> {safeText(reference.referenceId)} -{' '}
          {safeText(reference.safeSummary, 'No safe summary returned.')}
        </li>
      ))}
    </ul>
  );
}

function DetailSections({ detail }: { detail: DogfoodReceiptViewerDetail | null }) {
  if (!detail) {
    return null;
  }

  return (
    <div className="dogfood-receipt-detail-grid" data-testid="dogfood-receipts.boundaryWarnings">
      <div className="workflow-section">
        <h3>Observations</h3>
        <TextList items={detail.observations} empty="No safe observations were returned." />
      </div>
      <div className="workflow-section">
        <h3>Blocked reasons</h3>
        <TextList items={detail.blockedReasons} empty="No blocked reasons were returned." />
      </div>
      <div className="workflow-section">
        <h3>Referenced gate decisions</h3>
        <ReferenceList references={detail.referencedGateDecisions} testId="dogfood-receipts.gateRefs" />
      </div>
      <div className="workflow-section">
        <h3>Referenced tool requests</h3>
        <ReferenceList references={detail.referencedToolRequests} testId="dogfood-receipts.toolRequestRefs" />
      </div>
      <div className="workflow-section">
        <h3>Warnings and limitations</h3>
        <TextList items={[...detail.boundaryWarnings, ...detail.durabilityWarnings, ...detail.knownLimitations]} empty="No safe warnings were returned." />
      </div>
      <div className="workflow-section">
        <h3>Safe timeline summaries</h3>
        <TextList items={detail.safeTimelineSummaries} empty="No safe timeline summaries were returned." />
      </div>
    </div>
  );
}

function TextList({ items, empty }: { items: string[]; empty: string }) {
  if (items.length === 0) {
    return <p>{empty}</p>;
  }

  return (
    <ul className="detail-list">
      {items.map((item, index) => (
        <li key={`${item}-${index}`}>{safeText(item)}</li>
      ))}
    </ul>
  );
}

function hasSearchBasis(filters: DogfoodReceiptViewerFilters) {
  return Boolean(
    filters.projectReferenceId.trim() ||
      filters.dogfoodLoopId.trim() ||
      filters.dogfoodReceiptId.trim() ||
      filters.workflowRunId.trim() ||
      filters.workflowStepId.trim() ||
      filters.correlationId.trim()
  );
}

function canLoadReceiptDirectly(filters: DogfoodReceiptViewerFilters) {
  return Boolean(filters.projectReferenceId.trim() && (filters.dogfoodLoopId.trim() || filters.dogfoodReceiptId.trim()));
}

function receiptLookupId(filters: DogfoodReceiptViewerFilters) {
  return filters.dogfoodLoopId.trim() || filters.dogfoodReceiptId.trim();
}

function toTraceQuery(filters: DogfoodReceiptViewerFilters): GovernanceTraceQuery {
  const take = Number.parseInt(filters.take, 10);
  return {
    projectReferenceId: filters.projectReferenceId.trim(),
    workflowRunId: filters.workflowRunId.trim(),
    workflowStepId: filters.workflowStepId.trim(),
    correlationId: filters.correlationId.trim(),
    subjectReferenceId: filters.dogfoodReceiptId.trim() || filters.dogfoodLoopId.trim(),
    eventKind: 'dogfood.receipt.recorded',
    sourceComponent: filters.sourceComponent.trim(),
    fromUtc: filters.fromUtc.trim(),
    toUtc: filters.toUtc.trim(),
    take: Number.isFinite(take) ? take : 50
  };
}

function buildDetailFromReceipt(
  item: DogfoodReceiptViewerItem,
  receipt: DogfoodReceiptDetailData,
  warnings: string[]
): DogfoodReceiptViewerDetail {
  return {
    item,
    goal: safeText(receipt.goal, 'No safe goal returned.'),
    observations: safeTextArray(receipt.observations),
    blockedReasons: safeTextArray(receipt.blockedReasons),
    referencedAgentRuns: referencesFromReceipt(receipt.referencedAgentRuns, safeText),
    referencedCriticReviews: referencesFromReceipt(receipt.referencedCriticReviews, safeText),
    referencedMemoryImprovements: referencesFromReceipt(receipt.referencedMemoryImprovements, safeText),
    referencedToolRequests: referencesFromReceipt(receipt.referencedToolRequests, safeText),
    referencedGateDecisions: referencesFromReceipt(receipt.referencedGateDecisions, safeText),
    evidenceReferences: referencesFromReceipt(receipt.evidenceRefs, safeText),
    durabilityWarnings: safeTextArray(receipt.durabilityWarnings),
    knownLimitations: safeTextArray(receipt.knownLimitations),
    boundaryWarnings: safeTextArray([...(warnings ?? []), ...(receipt.warnings ?? [])]),
    safeTimelineSummaries: [],
    trace: null
  };
}

function buildDetailFromTrace(
  item: DogfoodReceiptViewerItem,
  trace: GovernanceTraceDetail | null,
  warnings: string[]
): DogfoodReceiptViewerDetail {
  const evidenceReferences: DogfoodReceiptReferenceView[] = (trace?.relatedReferences ?? []).map((reference) => ({
    referenceKind: safeText(reference.referenceKind, 'reference'),
    referenceId: safeText(reference.referenceId),
    safeSummary: safeText(reference.safeSummary, 'No safe summary returned.'),
    durable: true,
    backendRecorded: true,
    source: 'governance_trace'
  }));

  return {
    item,
    goal: 'Trace-discovered dogfood receipt evidence.',
    observations: [],
    blockedReasons: [],
    referencedAgentRuns: [],
    referencedCriticReviews: [],
    referencedMemoryImprovements: [],
    referencedToolRequests: [],
    referencedGateDecisions: [],
    evidenceReferences,
    durabilityWarnings: [],
    knownLimitations: [],
    boundaryWarnings: safeTextArray([...(warnings ?? []), ...(trace?.boundaryWarnings ?? [])]),
    safeTimelineSummaries: (trace?.timeline ?? []).map((timeline) => safeText(timeline.safeSummary, 'No safe timeline summary returned.')),
    trace
  };
}

function sanitizeReceiptData(receipt: DogfoodReceiptDetailData | null): DogfoodReceiptDetailData | null {
  if (!receipt) {
    return null;
  }

  return {
    dogfoodLoopId: safeText(receipt.dogfoodLoopId),
    runId: safeText(receipt.runId),
    receiptId: safeText(receipt.receiptId),
    evidenceId: safeText(receipt.evidenceId),
    projectId: safeText(receipt.projectId),
    summary: safeText(receipt.summary, 'No safe summary returned.'),
    goal: safeText(receipt.goal),
    observations: safeTextArray(receipt.observations),
    blockedReasons: safeTextArray(receipt.blockedReasons),
    referencedAgentRuns: sanitizeReferences(receipt.referencedAgentRuns),
    referencedCriticReviews: sanitizeReferences(receipt.referencedCriticReviews),
    referencedMemoryImprovements: sanitizeReferences(receipt.referencedMemoryImprovements),
    referencedToolRequests: sanitizeReferences(receipt.referencedToolRequests),
    referencedGateDecisions: sanitizeReferences(receipt.referencedGateDecisions),
    evidenceRefs: sanitizeReferences(receipt.evidenceRefs),
    durable: receipt.durable === true,
    containsNonDurableReferences: receipt.containsNonDurableReferences === true,
    durabilityWarnings: safeTextArray(receipt.durabilityWarnings),
    knownLimitations: safeTextArray(receipt.knownLimitations),
    createdAtUtc: safeText(receipt.createdAtUtc),
    warnings: safeTextArray(receipt.warnings)
  };
}

function sanitizeReferences(references: DogfoodReceiptDetailData['evidenceRefs']) {
  return (references ?? []).map((reference) => ({
    refType: safeText(reference.refType),
    refId: safeText(reference.refId),
    summary: safeText(reference.summary, 'No safe reference summary returned.'),
    durable: reference.durable === true,
    backendRecorded: reference.backendRecorded === true,
    source: safeText(reference.source)
  }));
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

function safeTextArray(values: unknown) {
  return Array.isArray(values) ? values.map((value) => safeText(value)).filter(Boolean) : [];
}

function extractIssues(error: unknown): Array<DogfoodLoopIssue | GovernanceTraceIssue> {
  if (!(error instanceof IronDevApiError)) {
    return [];
  }

  const dogfoodBody = error.body as Partial<DogfoodLoopApiEnvelope<DogfoodReceiptDetailData>> | undefined;
  const governanceBody = error.body as Partial<GovernanceTraceApiEnvelope<GovernanceTraceListData | GovernanceTraceDetailData>> | undefined;
  return [...(dogfoodBody?.errors ?? []), ...(governanceBody?.errors ?? []), ...(governanceBody?.data?.issues ?? [])];
}

function safeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = text.toLowerCase().replace(/[^a-z0-9 -]/g, '');
  return unsafeDogfoodReceiptTextMarkers.some((marker) => normalized.includes(marker)) ? redactedDogfoodReceiptText : text;
}

function openTraceMessage(item: DogfoodReceiptViewerItem | null) {
  return item?.traceId ? `/api/v1/governance/traces/${safeText(item.traceId)}` : 'Select a dogfood receipt with trace evidence before opening trace.';
}

function openTimelineMessage(item: DogfoodReceiptViewerItem | null) {
  return item?.correlationId ? `/governance/timeline?correlationId=${safeText(item.correlationId)}` : 'Select a receipt with correlation before opening timeline.';
}

function openCorrelationReportMessage(item: DogfoodReceiptViewerItem | null) {
  const query = item?.dogfoodReceiptId
    ? `dogfoodReceiptId=${safeText(item.dogfoodReceiptId)}`
    : item?.correlationId
      ? `correlationId=${safeText(item.correlationId)}`
      : '';

  return query
    ? `/api/v1/governance/correlation-reports/approval-gate-dogfood?${query}`
    : 'Select a receipt before opening correlation report.';
}

function openToolGateLedgerMessage(item: DogfoodReceiptViewerItem | null) {
  return item?.workflowRunId ? `/governance/tool-gates?workflowRunId=${safeText(item.workflowRunId)}` : 'Select a receipt with workflow run before opening tool gate ledger.';
}

function openApprovalPackageMessage(item: DogfoodReceiptViewerItem | null) {
  return item?.correlationId ? `/governance/approval-packages?correlationId=${safeText(item.correlationId)}` : 'Select a receipt with correlation before opening approval package.';
}
