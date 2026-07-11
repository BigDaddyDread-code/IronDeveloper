import { FormEvent, useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { AuditLedgerItem, AuditLedgerResponse } from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';

interface AuditSectionProps {
  projectId: number;
}

type AuditLedgerLoadState = 'loading' | 'ready' | 'empty' | 'unavailable';

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

export function AuditSection({ projectId }: AuditSectionProps) {
  const session = useSessionContext();
  const [filters, setFilters] = useState<AuditFilters>(emptyAuditFilters);
  const [appliedFilters, setAppliedFilters] = useState<AuditFilters>(emptyAuditFilters);
  const [ledger, setLedger] = useState<AuditLedgerResponse | null>(null);
  const [loadState, setLoadState] = useState<AuditLedgerLoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);

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
    setAppliedFilters(filters);
  };

  if (loadState === 'loading') {
    return <p className="fl-empty" data-testid="flow.audit.loading">Loading audit ledger...</p>;
  }

  if (loadState === 'unavailable') {
    return (
      <section className="fl-audit" data-testid="flow.library.auditLedger">
        <AuditHeader ledger={ledger} />
        <div className="fl-error" role="alert" data-testid="flow.audit.error">
          {errorMessage}
        </div>
        <button className="fl-btn" type="button" onClick={() => setReloadKey((current) => current + 1)}>
          Retry
        </button>
      </section>
    );
  }

  return (
    <section className="fl-audit" data-testid="flow.library.auditLedger" aria-labelledby="audit-heading">
      <AuditHeader ledger={ledger} />

      <form className="fl-audit-filters" onSubmit={applyFilters} aria-label="Audit ledger filters">
        <label className="fl-field">
          Actor
          <input
            value={filters.actor}
            onChange={(event) => setFilters((current) => ({ ...current, actor: event.target.value }))}
            data-testid="flow.audit.filter.actor"
          />
        </label>
        <label className="fl-field">
          Event
          <input
            value={filters.event}
            onChange={(event) => setFilters((current) => ({ ...current, event: event.target.value }))}
            data-testid="flow.audit.filter.event"
          />
        </label>
        <label className="fl-field">
          Work Item
          <input
            inputMode="numeric"
            value={filters.workItemId}
            onChange={(event) => setFilters((current) => ({ ...current, workItemId: event.target.value }))}
            data-testid="flow.audit.filter.workItem"
          />
        </label>
        <label className="fl-field">
          From
          <input
            type="datetime-local"
            value={filters.fromUtc}
            onChange={(event) => setFilters((current) => ({ ...current, fromUtc: event.target.value }))}
            data-testid="flow.audit.filter.from"
          />
        </label>
        <label className="fl-field">
          To
          <input
            type="datetime-local"
            value={filters.toUtc}
            onChange={(event) => setFilters((current) => ({ ...current, toUtc: event.target.value }))}
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
        <AuditRows items={ledger?.items ?? []} />
      )}
    </section>
  );
}

function AuditHeader({ ledger }: { ledger: AuditLedgerResponse | null }) {
  const statement = ledger?.boundary?.boundaryStatement;
  return (
    <header className="fl-audit-heading">
      <div>
        <p className="fl-plabel">Unified audit ledger</p>
        <h2 id="audit-heading">Project audit</h2>
        {statement ? <p>{statement}</p> : null}
      </div>
      <StatusBadge status="ready">{ledger?.returnedCount ?? 0} rows</StatusBadge>
    </header>
  );
}

function AuditRows({ items }: { items: AuditLedgerItem[] }) {
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
                {(item.evidenceLinks ?? []).map((link) => (
                  <a key={`${link.label}-${link.href}`} href={link.href ?? '#'} data-testid="flow.audit.evidence">
                    {safeText(link.label, 'Evidence')}
                  </a>
                ))}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
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
