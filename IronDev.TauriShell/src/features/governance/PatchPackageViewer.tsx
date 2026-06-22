import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import {
  patchPackageViewerBoundaryFields,
  patchPackageViewerBoundaryWarnings,
  type PatchPackageViewerModel,
  type PatchPackageViewerLoadStatus
} from './PatchPackageViewerTypes';

interface PatchPackageViewerProps {
  status: PatchPackageViewerLoadStatus;
  model: PatchPackageViewerModel | null;
  message: string;
}

export function PatchPackageViewer({ status, model, message }: PatchPackageViewerProps) {
  const boundary = model?.artifacts.boundary ?? model?.metadata.boundary ?? model?.envelopeBoundary ?? null;
  const warnings = unique([
    ...patchPackageViewerBoundaryWarnings,
    ...(model?.envelopeWarnings ?? []),
    ...(model?.artifacts.authorityWarnings ?? [])
  ]);

  if (status === 'loading') {
    return (
      <main className="patch-package-viewer-workspace" data-testid="patch-package.workspace">
        <section className="workspace-frame">
          <Surface testId="patch-package.loading">
            <EmptyState title="Loading patch package" body="Reading patch package evidence does not apply, approve, or continue workflow." />
          </Surface>
        </section>
      </main>
    );
  }

  if (status === 'missing' || !model) {
    return (
      <main className="patch-package-viewer-workspace" data-testid="patch-package.workspace">
        <section className="workspace-frame">
          <Surface testId={status === 'error' ? 'patch-package.error' : 'patch-package.empty'}>
            <EmptyState title="No patch package selected" body={message} />
          </Surface>
          <BoundaryBanner warnings={patchPackageViewerBoundaryWarnings} />
        </section>
      </main>
    );
  }

  const { metadata, artifacts } = model;
  const validationSections = [
    { title: 'What ran', items: artifacts.whatRan, testId: 'patch-package.validation.whatRan' },
    { title: 'What passed', items: artifacts.whatPassed, testId: 'patch-package.validation.whatPassed' },
    { title: 'What failed', items: artifacts.whatFailed, testId: 'patch-package.validation.whatFailed' },
    { title: 'What skipped', items: artifacts.whatWasSkipped, testId: 'patch-package.validation.whatSkipped' }
  ];

  return (
    <main className="patch-package-viewer-workspace" data-testid="patch-package.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div>
            <p className="eyebrow">Patch package evidence</p>
            <h1>Patch Package Viewer</h1>
            <p className="lede">Patch package artifacts are displayed for review only. They are not source apply authority.</p>
          </div>
          <StatusBadge status={artifacts.validationIsStale ? 'warning' : validationBadgeTone(artifacts.validationOutcome)}>
            {artifacts.validationIsStale ? 'Validation stale' : artifacts.validationOutcome}
          </StatusBadge>
        </div>

        <BoundaryBanner warnings={patchPackageViewerBoundaryWarnings} />

        <Surface className="patch-package-viewer-metadata" testId="patch-package.header">
          <Metadata label="Package ID" value={metadata.packageId} />
          <Metadata label="Repository" value={metadata.repository} />
          <Metadata label="Branch" value={metadata.branch} />
          <Metadata label="Run ID" value={metadata.runId} />
          <Metadata label="Patch hash" value={metadata.patchHash} />
        </Surface>

        <Surface className="patch-package-viewer-diff" testId="patch-package.patchDiff">
          <PanelHeader title="Patch diff" countLabel={`${lineCount(artifacts.patchDiffText)} line(s)`} />
          <pre>{artifacts.patchDiffText || 'No patch diff text returned.'}</pre>
        </Surface>

        <div className="patch-package-viewer-summary-grid">
          <TextPanel title="Review summary" text={artifacts.reviewSummaryText} testId="patch-package.reviewSummary" />
          <TextPanel title="Known risks" text={artifacts.knownRisksText} testId="patch-package.knownRisks" />
        </div>

        <Surface className="patch-package-viewer-validation" testId="patch-package.validationSummary">
          <div className="patch-package-viewer-panel-header">
            <div>
              <p className="eyebrow">Validation and tests</p>
              <h2>{artifacts.validationOutcome}</h2>
            </div>
            <StatusBadge status={artifacts.validationIsStale ? 'warning' : validationBadgeTone(artifacts.validationOutcome)}>
              Stale = {String(artifacts.validationIsStale)}
            </StatusBadge>
          </div>
          <pre>{artifacts.validationSummaryText || 'No validation summary text returned.'}</pre>
          <div className="patch-package-viewer-validation-grid">
            {validationSections.map((section) => (
              <ListPanel key={section.testId} title={section.title} items={section.items} testId={section.testId} />
            ))}
          </div>
        </Surface>

        <div className="patch-package-viewer-grid">
          <ListPanel title="Proposed files" items={unique([...metadata.proposedFilePaths, ...artifacts.proposedFilePaths])} testId="patch-package.proposedFiles" />
          <ListPanel title="Artifact refs" items={unique([...metadata.artifactRefs, ...artifacts.artifactRefs])} testId="patch-package.artifactRefs" />
          <ListPanel title="Evidence refs" items={unique([...metadata.evidenceRefs, ...artifacts.evidenceRefs])} testId="patch-package.evidenceRefs" warning="Evidence refs are not approval." />
          <ListPanel title="Receipt refs" items={unique([...metadata.receiptRefs, ...artifacts.receiptRefs])} testId="patch-package.receiptRefs" warning="Receipt refs are not authority." />
          <ListPanel title="Authority warnings" items={warnings} testId="patch-package.authorityWarnings" />
        </div>

        <Surface className="patch-package-viewer-boundary" testId="patch-package.boundary">
          <div className="patch-package-viewer-panel-header">
            <div>
              <p className="eyebrow">Read-only boundary</p>
              <h2>Boundary flags</h2>
            </div>
            <StatusBadge status={boundary?.readOnly === true ? 'ready' : 'danger'}>
              ReadOnly = {String(boundary?.readOnly === true)}
            </StatusBadge>
          </div>
          <div className="patch-package-viewer-boundary-grid">
            {patchPackageViewerBoundaryFields.map(([label, key, expected]) => {
              const value = boundary?.[key] === true;
              return (
                <div key={label} data-testid={`patch-package.boundary.${label}`}>
                  <dt>{label}</dt>
                  <dd className={value === expected ? 'is-expected' : 'is-violation'}>{String(value)}</dd>
                </div>
              );
            })}
          </div>
        </Surface>

        <footer className="patch-package-viewer-footer" data-testid="patch-package.footer">
          This viewer cannot apply source, execute rollback, approve, satisfy policy, commit, push, create PRs, mark ready,
          merge, release, deploy, promote memory, or continue workflow.
        </footer>
      </section>
    </main>
  );
}

function BoundaryBanner({ warnings }: { warnings: readonly string[] }) {
  return (
    <div className="patch-package-viewer-banner" data-testid="patch-package.boundaryBanner">
      {warnings.map((warning) => (
        <span key={warning}>{warning}</span>
      ))}
    </div>
  );
}

function TextPanel({ title, text, testId }: { title: string; text: string; testId: string }) {
  return (
    <Surface className="patch-package-viewer-panel" testId={testId}>
      <PanelHeader title={title} countLabel={`${lineCount(text)} line(s)`} />
      <pre>{text || `No ${title.toLowerCase()} text returned.`}</pre>
    </Surface>
  );
}

function ListPanel({ title, items, testId, warning }: { title: string; items: readonly string[]; testId: string; warning?: string }) {
  return (
    <Surface className="patch-package-viewer-panel" testId={testId}>
      <PanelHeader title={title} countLabel={`${items.length} item(s)`} />
      {warning ? <p className="patch-package-viewer-warning">{warning}</p> : null}
      {items.length === 0 ? (
        <p className="state-muted">No {title.toLowerCase()} returned.</p>
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

function PanelHeader({ title, countLabel }: { title: string; countLabel: string }) {
  return (
    <div className="patch-package-viewer-panel-header">
      <h2>{title}</h2>
      <span>{countLabel}</span>
    </div>
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

function validationBadgeTone(outcome: string) {
  const normalized = outcome.toLowerCase();
  if (normalized.includes('pass')) {
    return 'ready';
  }

  if (normalized.includes('fail')) {
    return 'danger';
  }

  if (normalized.includes('inconclusive') || normalized.includes('blocked')) {
    return 'warning';
  }

  return 'neutral';
}

function unique(values: readonly string[]) {
  return [...new Set(values.filter((value) => value.trim().length > 0))];
}

function lineCount(value: string) {
  return value ? value.split(/\r?\n/).length : 0;
}
