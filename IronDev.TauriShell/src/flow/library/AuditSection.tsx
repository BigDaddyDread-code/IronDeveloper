import { FormEvent, useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { AuditLedgerItem, AuditLedgerResponse, ProjectAuditExport } from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';
import { auditEventPath, libraryPath, navigateProductPath, safeProjectProductPath } from '../navigation/productRoutes';

interface AuditSectionProps {
  projectId: number;
  ledgerId?: string | null;
}

type AuditLedgerLoadState = 'loading' | 'ready' | 'empty' | 'unavailable';
type AuditExportState = 'idle' | 'generating' | 'ready' | 'error';

interface AuditFilters {
  actor: string;
  event: string;
  workItemId: string;
  fromUtc: string;
  toUtc: string;
}

const emptyAuditFilters: AuditFilters = {
  actor: '',
  event: '',
  workItemId: '',
  fromUtc: '',
  toUtc: ''
};

export function AuditSection({ projectId, ledgerId = null }: AuditSectionProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<AuditFilters>(emptyAuditFilters);
  const [appliedFilters, setAppliedFilters] = useState<AuditFilters>(emptyAuditFilters);
  const [ledger, setLedger] = useState<AuditLedgerResponse | null>(null);
  const [loadState, setLoadState] = useState<AuditLedgerLoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const [exportOpen, setExportOpen] = useState(false);
  const [exportState, setExportState] = useState<AuditExportState>('idle');
  const [exportPackage, setExportPackage] = useState<ProjectAuditExport | null>(null);
  const [exportError, setExportError] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoadState('loading');
      setErrorMessage('');
      try {
        const response = await session.client.searchAuditLedger(
          {
            projectId,
            actor: textOrUndefined(appliedFilters.actor),
            event: textOrUndefined(appliedFilters.event),
            workItemId: numberOrUndefined(appliedFilters.workItemId),
            fromUtc: dateTimeOrUndefined(appliedFilters.fromUtc),
            toUtc: dateTimeOrUndefined(appliedFilters.toUtc),
            take: 100
          },
          controller.signal
        );
        setLedger(response);
        setLoadState((response.items?.length ?? 0) === 0 ? 'empty' : 'ready');
      } catch (error) {
        if (controller.signal.aborted) return;
        setLoadState('unavailable');
        setErrorMessage(describeError(error, 'The audit ledger could not be loaded.'));
      }
    };
    void load();
    return () => controller.abort();
  }, [appliedFilters, projectId, reloadKey, session.client]);

  const applyFilters = (event: FormEvent) => {
    event.preventDefault();
    invalidateExport();
    setAppliedFilters(filters);
  };

  const updateFilter = (field: keyof AuditFilters, value: string) => {
    setFilters((current) => ({ ...current, [field]: value }));
    invalidateExport();
  };

  const invalidateExport = () => {
    setExportPackage(null);
    setExportError('');
    setExportState('idle');
  };

  const generateExport = async () => {
    setExportState('generating');
    setExportError('');
    setExportPackage(null);
    try {
      setExportPackage(await session.client.exportProjectAudit(projectId, exportFilters(appliedFilters)));
      setExportState('ready');
    } catch (error) {
      setExportState('error');
      setExportError(describeError(error, 'The audit export could not be generated.'));
    }
  };

  const downloadExport = () => {
    if (!exportPackage) return;
    const blob = new Blob([`${JSON.stringify(exportPackage, null, 2)}\n`], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = exportFileName(projectId, exportPackage.generatedUtc);
    anchor.click();
    URL.revokeObjectURL(url);
  };

  if (loadState === 'loading') {
    return <p className="fl-empty" data-testid="flow.audit.loading">Loading audit ledger...</p>;
  }

  if (loadState === 'unavailable') {
    return (
      <section className="fl-audit" data-testid="flow.library.auditLedger">
        <AuditHeader ledger={ledger} onExport={() => setExportOpen(true)} />
        <div className="fl-error" role="alert" data-testid="flow.audit.error">
          {errorMessage}
        </div>
        <button className="fl-btn" type="button" onClick={() => setReloadKey((current) => current + 1)}>
          Retry
        </button>
      </section>
    );
  }


  if (ledgerId) {
    const item = (ledger?.items ?? []).find((candidate) => candidate.ledgerId === ledgerId);
    return (
      <section className="fl-audit" data-testid="flow.library.auditLedger" aria-labelledby="audit-heading">
        <AuditHeader ledger={ledger} onExport={() => setExportOpen(true)} />
        <AuditEventDetail projectId={projectId} ledger={ledger} item={item} ledgerId={ledgerId} />
      </section>
    );
  }

  return (
    <section className="fl-audit" data-testid="flow.library.auditLedger" aria-labelledby="audit-heading">
      <AuditHeader ledger={ledger} onExport={() => setExportOpen(true)} />

      <form className="fl-audit-filters" onSubmit={applyFilters} aria-label="Audit ledger filters">
        <label className="fl-field">
          Actor
          <input
            value={filters.actor}
            onChange={(event) => updateFilter('actor', event.target.value)}
            data-testid="flow.audit.filter.actor"
          />
        </label>
        <label className="fl-field">
          Event
          <input
            value={filters.event}
            onChange={(event) => updateFilter('event', event.target.value)}
            data-testid="flow.audit.filter.event"
          />
        </label>
        <label className="fl-field">
          Work Item
          <input
            inputMode="numeric"
            value={filters.workItemId}
            onChange={(event) => updateFilter('workItemId', event.target.value)}
            data-testid="flow.audit.filter.workItem"
          />
        </label>
        <label className="fl-field">
          From
          <input
            type="datetime-local"
            value={filters.fromUtc}
            onChange={(event) => updateFilter('fromUtc', event.target.value)}
            data-testid="flow.audit.filter.from"
          />
        </label>
        <label className="fl-field">
          To
          <input
            type="datetime-local"
            value={filters.toUtc}
            onChange={(event) => updateFilter('toUtc', event.target.value)}
            data-testid="flow.audit.filter.to"
          />
        </label>
        <div className="fl-audit-actions">
          <button className="fl-btn fl-pri" type="submit" data-testid="flow.audit.filter.apply">
            Filter
          </button>
          <button className="fl-btn" type="button" onClick={() => setReloadKey((current) => current + 1)} data-testid="flow.audit.refresh">
            Refresh
          </button>
        </div>
      </form>

      {ledger?.issues?.length ? (
        <div className="fl-error" role="alert" data-testid="flow.audit.issues">
          {ledger.issues.map((issue) => issue.message || issue.code || 'Audit ledger request was rejected.').join(' ')}
        </div>
      ) : null}

      {loadState === 'empty' ? (
        <p className="fl-empty" data-testid="flow.audit.empty">No audit rows matched the current filters.</p>
      ) : (
        <AuditRows projectId={projectId} items={ledger?.items ?? []} />
      )}

      {exportOpen ? (
        <div className="fl-audit-export-backdrop">
          <section className="fl-audit-export" role="dialog" aria-modal="true" aria-labelledby="audit-export-heading" data-testid="flow.audit.export.dialog">
            <header>
              <div>
                <p className="fl-plabel">Read-only JSON package</p>
                <h3 id="audit-export-heading">Export current audit view</h3>
              </div>
              <button className="fl-btn" type="button" onClick={() => setExportOpen(false)}>Close</button>
            </header>

            <dl className="fl-audit-export__scope">
              <div><dt>Actor</dt><dd>{appliedFilters.actor || 'Any actor'}</dd></div>
              <div><dt>Event</dt><dd>{appliedFilters.event || 'Any event'}</dd></div>
              <div><dt>Work Item</dt><dd>{appliedFilters.workItemId ? `WI-${appliedFilters.workItemId}` : 'Any Work Item'}</dd></div>
              <div><dt>Date range</dt><dd>{formatDateRange(appliedFilters)}</dd></div>
              <div><dt>Maximum rows</dt><dd>250</dd></div>
            </dl>

            {exportState === 'error' ? <div className="fl-error" role="alert">{exportError}</div> : null}
            {exportPackage ? (
              <div className="fl-audit-export__result" role="status" data-testid="flow.audit.export.result">
                <div><span>Rows</span><strong>{exportPackage.returnedCount ?? 0}</strong></div>
                <div><span>Truncated</span><strong>{exportPackage.truncated ? 'Yes' : 'No'}</strong></div>
                <div><span>Schema</span><strong>{exportPackage.schemaVersion ?? 'Unknown'}</strong></div>
                <div><span>Generated</span><strong>{formatTime(exportPackage.generatedUtc)}</strong></div>
                <p><span>Items SHA-256</span><code>{exportPackage.itemsSha256 ?? 'Unavailable'}</code></p>
              </div>
            ) : (
              <p className="fl-muted">Generate the backend package to confirm row count, truncation, schema, and item hash before downloading.</p>
            )}

            <p className="fl-audit-export__boundary">
              {exportPackage?.boundary?.boundaryStatement ?? 'The export is bounded, non-secret, and read-only. It grants no authority.'}
            </p>

            <footer>
              <button className="fl-btn" type="button" onClick={() => void generateExport()} disabled={exportState === 'generating'} data-testid="flow.audit.export.generate">
                {exportState === 'generating' ? 'Generating...' : exportPackage ? 'Regenerate export' : 'Generate export'}
              </button>
              <button className="fl-btn fl-pri" type="button" onClick={downloadExport} disabled={!exportPackage} data-testid="flow.audit.export.download">
                Download JSON
              </button>
            </footer>
          </section>
        </div>
      ) : null}
    </section>
  );
}

function AuditHeader({ ledger, onExport }: { ledger: AuditLedgerResponse | null; onExport: () => void }) {
  const statement = ledger?.boundary?.boundaryStatement;
  return (
    <header className="fl-audit-heading">
      <div>
        <p className="fl-plabel">Unified audit ledger</p>
        <h2 id="audit-heading">Project audit</h2>
        {statement ? <p>{statement}</p> : null}
      </div>
      <div className="fl-audit-heading__actions">
        <StatusBadge status="ready">{ledger?.returnedCount ?? 0} rows</StatusBadge>
        <button className="fl-btn" type="button" onClick={onExport} data-testid="flow.audit.export.open">Export current view</button>
      </div>
    </header>
  );
}

function AuditRows({ projectId, items }: { projectId: number; items: AuditLedgerItem[] }) {
  return (
    <div className="fl-audit-table-wrap" data-testid="flow.audit.rows">
      <table className="fl-table fl-audit-table">
        <thead>
          <tr>
            <th>Time</th>
            <th>Actor</th>
            <th>Work Item</th>
            <th>Event</th>
            <th>Outcome</th>
            <th>Evidence</th>
            <th><span className="fl-visually-hidden">Inspect</span></th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.ledgerId ?? `${item.timeUtc}-${item.action}`} data-testid="flow.audit.row">
              <td>{formatTime(item.timeUtc)}</td>
              <td>
                <strong>{safeText(item.actorDisplayName, 'Unknown actor')}</strong>
                <small>{safeText(item.actorId, 'unknown')}</small>
              </td>
              <td>
                {item.workItemId ? (
                  <>
                    <strong>WI-{item.workItemId}</strong>
                    <small>{safeText(item.workItemTitle, 'Untitled Work Item')}</small>
                  </>
                ) : (
                  <span className="fl-muted">Project</span>
                )}
              </td>
              <td>
                <strong>{safeText(item.action, 'Recorded')}</strong>
                <small>{safeText(item.summary, safeText(item.source, 'Audit event'))}</small>
              </td>
              <td>{safeText(item.outcome, 'Recorded')}</td>
              <td>
                <EvidenceLinks projectId={projectId} links={item.evidenceLinks ?? []} />
              </td>
              <td>
                {item.ledgerId ? (
                  <button
                    className="fl-audit__inspect"
                    type="button"
                    onClick={() => navigateProductPath(auditEventPath(projectId, item.ledgerId!))}
                    data-testid="flow.audit.inspect"
                  >
                    Inspect
                  </button>
                ) : <span className="fl-muted">Unavailable</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function AuditEventDetail({
  projectId,
  ledger,
  item,
  ledgerId
}: {
  projectId: number;
  ledger: AuditLedgerResponse | null;
  item: AuditLedgerItem | undefined;
  ledgerId: string;
}) {
  if (!item) {
    return (
      <div className="fl-audit-detail" data-testid="flow.audit.detail.missing">
        <button className="fl-btn" type="button" onClick={() => navigateProductPath(libraryPath(projectId, 'audit'))}>Back to Audit</button>
        <div className="fl-empty">
          <h3>Event not in the current bounded result</h3>
          <p>The event <code>{ledgerId}</code> was not returned by the current Audit query. No details have been inferred.</p>
        </div>
      </div>
    );
  }

  return (
    <article className="fl-audit-detail" data-testid="flow.audit.detail">
      <header>
        <button className="fl-btn" type="button" onClick={() => navigateProductPath(libraryPath(projectId, 'audit'))}>Back to Audit</button>
        <StatusBadge status="ready">{safeText(item.outcome, 'Recorded')}</StatusBadge>
      </header>
      <div>
        <p className="fl-plabel">Audit event</p>
        <h3>{safeText(item.action, 'Recorded event')}</h3>
        <p>{safeText(item.summary, 'No event summary was returned.')}</p>
      </div>
      <dl className="fl-audit-detail__facts">
        <div><dt>Time</dt><dd>{formatTime(item.timeUtc)}</dd></div>
        <div><dt>Actor</dt><dd>{safeText(item.actorDisplayName, 'Unknown actor')} <small>{safeText(item.actorId, 'unknown')}</small></dd></div>
        <div><dt>Scope</dt><dd>{item.workItemId ? `WI-${item.workItemId} ${safeText(item.workItemTitle, '')}` : safeText(item.projectName, `Project ${projectId}`)}</dd></div>
        <div><dt>Source</dt><dd>{safeText(item.source, 'Unknown source')}</dd></div>
        <div><dt>Correlation</dt><dd><code>{safeText(item.correlationId, 'Not returned')}</code></dd></div>
        <div><dt>Ledger ID</dt><dd><code>{safeText(item.ledgerId, ledgerId)}</code></dd></div>
      </dl>
      <section className="fl-audit-detail__evidence">
        <h4>Evidence</h4>
        {(item.evidenceLinks?.length ?? 0) > 0
          ? <EvidenceLinks projectId={projectId} links={item.evidenceLinks ?? []} />
          : <p className="fl-muted">No evidence target was returned for this event.</p>}
      </section>
      <p className="fl-audit-export__boundary">
        {ledger?.boundary?.boundaryStatement ?? 'Audit inspection is read-only. It grants no authority.'}
      </p>
    </article>
  );
}

function EvidenceLinks({ projectId, links }: { projectId: number; links: AuditLedgerItem['evidenceLinks'] }) {
  return (
    <div className="fl-audit__evidence-links">
      {(links ?? []).map((link) => {
        const target = safeProjectProductPath(link.href, projectId);
        const label = safeText(link.label, 'Evidence');
        return target ? (
          <a
            key={`${label}-${link.href}`}
            href={target}
            onClick={(event) => { event.preventDefault(); navigateProductPath(target); }}
            data-testid="flow.audit.evidence"
          >
            {label}
          </a>
        ) : (
          <span key={`${label}-${link.href}`} className="fl-muted" data-testid="flow.audit.evidence.unavailable">
            {label}: unavailable evidence target
          </span>
        );
      })}
    </div>
  );
}

function textOrUndefined(value: string) {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

function numberOrUndefined(value: string) {
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : undefined;
}

function dateTimeOrUndefined(value: string) {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

function exportFilters(filters: AuditFilters) {
  return {
    actor: textOrUndefined(filters.actor),
    event: textOrUndefined(filters.event),
    workItemId: numberOrUndefined(filters.workItemId),
    fromUtc: dateTimeOrUndefined(filters.fromUtc),
    toUtc: dateTimeOrUndefined(filters.toUtc),
    take: 250
  };
}

function formatDateRange(filters: AuditFilters) {
  const from = dateTimeOrUndefined(filters.fromUtc);
  const to = dateTimeOrUndefined(filters.toUtc);
  if (!from && !to) return 'Any time';
  return `${from ? formatTime(from) : 'Beginning'} to ${to ? formatTime(to) : 'Now'}`;
}

function exportFileName(projectId: number, generatedUtc: string | null | undefined) {
  const parsed = generatedUtc ? new Date(generatedUtc) : new Date();
  const safeDate = Number.isNaN(parsed.getTime()) ? new Date() : parsed;
  const stamp = safeDate.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z').replace('T', '-');
  return `irondev-audit-project-${projectId}-${stamp}.json`;
}

function formatTime(value: string | null | undefined) {
  if (!value) return 'Unknown time';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function safeText(value: string | number | null | undefined, fallback: string) {
  const text = value === null || value === undefined ? '' : String(value).trim();
  return text.length > 0 ? text : fallback;
}

function describeError(error: unknown, fallback: string) {
  if (error instanceof IronDevApiError) {
    const body = error.body as { message?: string; error?: string; detail?: string; issues?: Array<{ message?: string }> } | undefined;
    const issueMessages = body?.issues
      ?.map((issue) => issue.message)
      .filter((message): message is string => Boolean(message?.trim()));
    if (issueMessages?.length) return issueMessages.join(' ');
    return body?.message ?? body?.error ?? body?.detail ?? error.message ?? fallback;
  }
  return error instanceof Error ? error.message : fallback;
}
