import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import type { FrontendReadBoundary } from '../../api/types';
import {
  operationStatusViewerBoundaryFields,
  operationStatusViewerBoundaryWarnings,
  type OperationStatusViewerModel
} from './OperationStatusViewerTypes';

interface OperationStatusViewerProps {
  status: 'idle' | 'loading' | 'ready' | 'missing' | 'error';
  model: OperationStatusViewerModel | null;
  message: string;
}

export function OperationStatusViewer({ status, model, message }: OperationStatusViewerProps) {
  const boundary = model?.boundary ?? model?.envelopeBoundary ?? null;
  const warnings = unique([
    ...operationStatusViewerBoundaryWarnings,
    ...(model?.envelopeWarnings ?? []),
    ...(model?.authorityWarnings ?? [])
  ]);

  if (status === 'loading') {
    return (
      <main className="operation-status-viewer-workspace" data-testid="operation-status.workspace">
        <section className="workspace-frame">
          <Surface testId="operation-status.loading">
            <EmptyState title="Loading operation status" body="Reading status does not approve, execute, mutate, or continue workflow." />
          </Surface>
        </section>
      </main>
    );
  }

  if (status === 'missing' || !model) {
    return (
      <main className="operation-status-viewer-workspace" data-testid="operation-status.workspace">
        <section className="workspace-frame">
          <Surface testId={status === 'error' ? 'operation-status.error' : 'operation-status.empty'}>
            <EmptyState title="No operation status selected" body={message} />
          </Surface>
          <BoundaryBanner warnings={operationStatusViewerBoundaryWarnings} />
        </section>
      </main>
    );
  }

  const sections = [
    {
      id: 'blockedReasons',
      title: 'Blocked reasons',
      emptyText: 'No blocked reasons returned.',
      items: model.blockedReasons
    },
    {
      id: 'missingEvidence',
      title: 'Missing evidence',
      emptyText: 'No missing evidence returned.',
      items: model.missingEvidence
    },
    {
      id: 'nextSafeActions',
      title: 'Next safe action — guidance only',
      emptyText: 'No next safe action returned.',
      items: model.nextSafeActions
    },
    {
      id: 'forbiddenActions',
      title: 'Forbidden actions',
      emptyText: 'No forbidden actions returned.',
      items: model.forbiddenActions
    },
    {
      id: 'evidenceRefs',
      title: 'Evidence refs',
      emptyText: 'No evidence refs returned.',
      items: model.evidenceRefs
    },
    {
      id: 'receiptRefs',
      title: 'Receipt refs',
      emptyText: 'No receipt refs returned.',
      items: model.receiptRefs
    },
    {
      id: 'authorityWarnings',
      title: 'Authority warnings',
      emptyText: 'No authority warnings returned.',
      items: warnings
    }
  ];

  return (
    <main className="operation-status-viewer-workspace" data-testid="operation-status.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div>
            <p className="eyebrow">Governed operation status</p>
            <h1>Operation Status Viewer</h1>
            <p className="lede">Backend status is displayed as read-only truth. It is not permission to act.</p>
          </div>
          <StatusBadge status={stateBadgeTone(model.state)}>{model.state}</StatusBadge>
        </div>

        <BoundaryBanner warnings={operationStatusViewerBoundaryWarnings} />

        <Surface className="operation-status-viewer-identity" testId="operation-status.header">
          <Metadata label="Operation ID" value={model.operationId} />
          <Metadata label="Operation kind" value={model.operationKind} />
          <Metadata label="Subject" value={model.subject} />
          <Metadata label="Observed" value={displayDate(model.observedAtUtc)} />
          <Metadata label="Expires" value={model.expiresAtUtc ? displayDate(model.expiresAtUtc) : 'No expiry supplied'} />
        </Surface>

        <Surface className="operation-status-viewer-state" testId="operation-status.state">
          <div>
            <p className="eyebrow">State</p>
            <h2>State: {model.state}</h2>
          </div>
          {model.state.toLowerCase() === 'eligible' ? (
            <p data-testid="operation-status.eligibleWarning">
              Eligible is displayed as backend state only. It is not ready to execute.
            </p>
          ) : null}
        </Surface>

        <div className="operation-status-viewer-grid">
          {sections.map((section) => (
            <StatusSection
              key={section.id}
              title={section.title}
              items={section.items}
              emptyText={section.emptyText}
              testId={`operation-status.${section.id}`}
            />
          ))}
        </div>

        <Surface className="operation-status-viewer-boundary" testId="operation-status.boundary">
          <div className="operation-status-viewer-panel-header">
            <div>
              <p className="eyebrow">Read-only boundary</p>
              <h2>Boundary flags</h2>
            </div>
            <StatusBadge status={boundary?.readOnly === true ? 'ready' : 'danger'}>
              ReadOnly = {String(boundary?.readOnly === true)}
            </StatusBadge>
          </div>
          <div className="operation-status-viewer-boundary-grid">
            {operationStatusViewerBoundaryFields.map(([label, key, expected]) => {
              const value = boundary?.[key] === true;
              return (
                <div key={label} data-testid={`operation-status.boundary.${label}`}>
                  <dt>{label}</dt>
                  <dd className={value === expected ? 'is-expected' : 'is-violation'}>{String(value)}</dd>
                </div>
              );
            })}
          </div>
        </Surface>

        <footer className="operation-status-viewer-footer" data-testid="operation-status.footer">
          This viewer cannot apply, approve, accept approval, satisfy policy, rollback, commit, push, create PRs, mark
          ready, merge, release, deploy, promote memory, or continue workflow.
        </footer>
      </section>
    </main>
  );
}

function BoundaryBanner({ warnings }: { warnings: readonly string[] }) {
  return (
    <div className="operation-status-viewer-banner" data-testid="operation-status.boundaryBanner">
      {warnings.map((warning) => (
        <span key={warning}>{warning}</span>
      ))}
    </div>
  );
}

function StatusSection({ title, items, emptyText, testId }: { title: string; items: readonly string[]; emptyText: string; testId: string }) {
  const sectionWarning =
    title === 'Evidence refs'
      ? 'Evidence refs are not approval.'
      : title === 'Receipt refs'
        ? 'Receipt refs are not authority.'
        : title.startsWith('Next safe action')
          ? 'Guidance only. This is not an executable control.'
          : null;

  return (
    <Surface className="operation-status-viewer-panel" testId={testId}>
      <div className="operation-status-viewer-panel-header">
        <h2>{title}</h2>
        <span>{items.length} item(s)</span>
      </div>
      {sectionWarning ? <p className="operation-status-viewer-warning">{sectionWarning}</p> : null}
      {items.length === 0 ? (
        <p className="state-muted">{emptyText}</p>
      ) : (
        <ul>
          {items.map((item, index) => (
            <li key={`${item}-${index}`}>{item}</li>
          ))}
        </ul>
      )}
    </Surface>
  );
}

function Metadata({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function unique(values: readonly string[]) {
  return [...new Set(values.filter((value) => value.trim().length > 0))];
}

function displayDate(value: string) {
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? new Date(timestamp).toLocaleString() : value;
}

function stateBadgeTone(state: string) {
  const normalized = state.toLowerCase();
  if (normalized === 'blocked' || normalized === 'expired') {
    return 'warning';
  }

  if (normalized === 'failed') {
    return 'danger';
  }

  if (normalized === 'completed') {
    return 'ready';
  }

  return 'neutral';
}
