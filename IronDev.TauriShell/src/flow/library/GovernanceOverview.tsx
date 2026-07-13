import { useEffect, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ProjectGovernanceAttentionItem,
  ProjectGovernanceControl,
  ProjectGovernanceDecision,
  ProjectGovernanceException,
  ProjectGovernanceOverview as ProjectGovernanceOverviewModel
} from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';
import { RouteOutcomeScreen } from '../components/RouteOutcomeScreen';
import { governancePath, navigateProductPath, safeProjectProductPath, type GovernanceSection } from '../navigation/productRoutes';
import { governanceViewers } from './governanceRoutes';

type LoadState = 'loading' | 'ready' | 'notFound' | 'unavailable';

export function GovernanceOverview({ projectId, section = 'overview' }: { projectId: number; section?: GovernanceSection }) {
  const session = useSessionContext();
  const [model, setModel] = useState<ProjectGovernanceOverviewModel | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoadState('loading');
      setErrorMessage('');
      try {
        setModel(await session.client.getProjectGovernanceOverview(projectId, controller.signal));
        setLoadState('ready');
      } catch (error) {
        if (controller.signal.aborted) return;
        setModel(null);
        setLoadState(error instanceof IronDevApiError && error.status === 404 ? 'notFound' : 'unavailable');
        setErrorMessage(describeError(error));
      }
    };
    void load();
    return () => controller.abort();
  }, [projectId, reloadKey, session.client]);

  if (loadState === 'loading') {
    return <p className="fl-empty" data-testid="flow.governance.loading" role="status">Loading project governance...</p>;
  }

  if (loadState === 'notFound') {
    return (
      <RouteOutcomeScreen
        kind="notFound"
        title="Project governance not found"
        message="The backend did not return this project in the selected tenant."
        nextSafeAction="Return to Projects and select a project returned by the backend."
        actionLabel="Back to Projects"
        onAction={() => navigateProductPath('/projects')}
      />
    );
  }

  if (loadState === 'unavailable' || !model) {
    return (
      <RouteOutcomeScreen
        kind="unavailable"
        title="Governance is unavailable"
        message={errorMessage || 'The governance overview could not be loaded.'}
        nextSafeAction="Retry the backend-owned overview. No decision or action has been performed."
        actionLabel="Retry"
        onAction={() => setReloadKey((value) => value + 1)}
      />
    );
  }

  return <GovernanceOverviewContent model={model} section={section} />;
}

function GovernanceOverviewContent({ model, section }: { model: ProjectGovernanceOverviewModel; section: GovernanceSection }) {
  const projectId = model.projectId ?? 0;
  const attention = model.attentionItems ?? [];
  const controls = model.controls ?? [];
  const exceptions = model.exceptions ?? [];
  const decisions = model.recentDecisions ?? [];
  const sectionIssues = model.sectionIssues ?? [];
  const groupedControls = useMemo(() => groupControls(controls), [controls]);
  const primaryTarget = safeProductRoute(model.primaryAction.targetRoute, projectId);
  const auditTarget = safeProductRoute(model.navigation.audit, projectId);
  const settingsTarget = safeProductRoute(model.navigation.settings, projectId);

  return (
    <main className="fl-governance" data-testid="flow.governance.overview">
      <header className="fl-governance__header">
        <div className="fl-governance__identity">
          <p className="fl-plabel">Project governance</p>
          <div className="fl-governance__title-row">
            <h2>{text(model.projectName, 'Project governance')}</h2>
            <StatusBadge status={statusTone(model.overallStatus)} data-testid="flow.governance.status">
              {statusLabel(model.overallStatus)}
            </StatusBadge>
          </div>
          <p className="fl-governance__summary">{text(model.statusSummary, 'The backend returned no status summary.')}</p>
          <p className="fl-governance__generated">Generated {formatTime(model.generatedUtc)}</p>
        </div>
        <div className="fl-governance__actions" aria-label="Governance navigation">
          {primaryTarget ? (
            <button
              className="fl-btn fl-pri"
              type="button"
              onClick={() => navigateProductPath(primaryTarget)}
              data-testid="flow.governance.primaryAction"
            >
              {text(model.primaryAction.label, 'Review next item')}
            </button>
          ) : null}
          {auditTarget ? <button className="fl-btn" type="button" onClick={() => navigateProductPath(auditTarget)}>View audit</button> : null}
          {settingsTarget ? <button className="fl-btn" type="button" onClick={() => navigateProductPath(settingsTarget)}>Open settings</button> : null}
        </div>
      </header>

      <nav className="fl-governance__subnav" aria-label="Governance views">
        {governanceSections.map((candidate) => {
          const target = governanceSectionTarget(model, candidate.id, projectId);
          return (
            <button
              key={candidate.id}
              type="button"
              className={section === candidate.id ? 'fl-on' : ''}
              aria-current={section === candidate.id ? 'page' : undefined}
              onClick={() => navigateProductPath(target)}
            >
              {candidate.label}
            </button>
          );
        })}
      </nav>

      {section !== 'overview' ? (
        <div className="fl-governance__detail-heading">
          <button type="button" className="fl-governance__text-action" onClick={() => navigateProductPath(governanceSectionTarget(model, 'overview', projectId))}>
            Back to overview
          </button>
          <p>{detailDescription(section)}</p>
        </div>
      ) : null}

      {sectionIssues.length > 0 ? (
        <section className="fl-governance__section-issues" aria-labelledby="governance-section-issues" role="status">
          <h3 id="governance-section-issues">Some governance evidence is unavailable</h3>
          <ul>{sectionIssues.map((issue, index) => <li key={`${issue.section ?? 'section'}-${index}`}>{text(issue.summary, 'A section could not be evaluated.')}</li>)}</ul>
        </section>
      ) : null}

      {section === 'overview' ? <GovernanceSection
        title="Needs attention"
        count={attention.length}
        actionLabel="View exceptions"
        actionTarget={safeProductRoute(model.navigation.exceptions, projectId)}
        testId="flow.governance.attention"
      >
        {attention.length === 0 ? (
          <EmptySection>No governance action is waiting.</EmptySection>
        ) : (
          <div className="fl-governance__attention-list">
            {attention.map((item) => <AttentionCard key={`${item.workItemId}-${item.kind}`} item={item} projectId={projectId} />)}
          </div>
        )}
      </GovernanceSection> : null}

      {section === 'overview' || section === 'controls' ? <GovernanceSection
        title="Effective controls"
        count={controls.length}
        actionLabel="View all controls"
        actionTarget={safeProductRoute(model.navigation.controls, projectId)}
        testId="flow.governance.controls"
      >
        <div className="fl-governance__control-groups">
          {groupedControls.map(([group, entries]) => (
            <section key={group} className="fl-governance__control-group" aria-label={group}>
              <h4>{group}</h4>
              <dl>
                {entries.map((control) => (
                  <div key={control.id ?? control.label} className="fl-governance__control-row">
                    <dt>
                      <span>{text(control.label, 'Control')}</span>
                      <small>{text(control.source, 'Unavailable')}</small>
                    </dt>
                    <dd>
                      <strong>{text(control.effectiveValue, 'Unavailable')}</strong>
                      <span>{text(control.explanation, 'No explanation was returned.')}</span>
                    </dd>
                  </div>
                ))}
              </dl>
            </section>
          ))}
        </div>
      </GovernanceSection> : null}

      {section === 'overview' ? <div className="fl-governance__lower-grid">
        <GovernanceSection
          title="Exceptions and degraded states"
          count={exceptions.length}
          actionLabel="View all exceptions"
          actionTarget={safeProductRoute(model.navigation.exceptions, projectId)}
          testId="flow.governance.exceptions"
        >
          {exceptions.length === 0 ? <EmptySection>No active governance exceptions are recorded.</EmptySection> : (
            <div className="fl-governance__compact-list">{exceptions.slice(0, 5).map((item) => <ExceptionRow key={item.id ?? `${item.category}-${item.workItemId}`} item={item} projectId={projectId} />)}</div>
          )}
        </GovernanceSection>

        <GovernanceSection
          title="Recent decisions"
          count={decisions.length}
          actionLabel="View decisions"
          actionTarget={safeProductRoute(model.navigation.decisions, projectId)}
          testId="flow.governance.decisions"
        >
          {decisions.length === 0 ? <EmptySection>No consequential decisions were returned.</EmptySection> : (
            <div className="fl-governance__compact-list">{decisions.slice(0, 5).map((item) => <DecisionRow key={item.id ?? `${item.kind}-${item.workItemId}`} item={item} projectId={projectId} />)}</div>
          )}
        </GovernanceSection>
      </div> : null}

      {section === 'exceptions' ? (
        <GovernanceSection
          title="Exceptions and degraded states"
          count={exceptions.length}
          actionLabel="View audit"
          actionTarget={auditTarget}
          testId="flow.governance.exceptions.detail"
        >
          {exceptions.length === 0 ? <EmptySection>No active governance exceptions are recorded.</EmptySection> : (
            <div className="fl-governance__detail-list">{exceptions.map((item) => <ExceptionRow key={item.id ?? `${item.category}-${item.workItemId}`} item={item} projectId={projectId} />)}</div>
          )}
        </GovernanceSection>
      ) : null}

      {section === 'decisions' ? (
        <GovernanceSection
          title="Consequential decisions"
          count={decisions.length}
          actionLabel="View full audit"
          actionTarget={auditTarget}
          testId="flow.governance.decisions.detail"
        >
          {decisions.length === 0 ? <EmptySection>No consequential decisions were returned.</EmptySection> : (
            <div className="fl-governance__detail-list">{decisions.map((item) => <DecisionRow key={item.id ?? `${item.kind}-${item.workItemId}`} item={item} projectId={projectId} />)}</div>
          )}
        </GovernanceSection>
      ) : null}

      {section === 'technical' ? <TechnicalEvidence /> : null}

      <p className="fl-governance__boundary" data-testid="flow.governance.boundary">{text(model.boundary, 'Governance is read-only.')}</p>
    </main>
  );
}

const governanceSections: Array<{ id: GovernanceSection; label: string }> = [
  { id: 'overview', label: 'Overview' },
  { id: 'controls', label: 'Controls' },
  { id: 'exceptions', label: 'Exceptions' },
  { id: 'decisions', label: 'Decisions' },
  { id: 'technical', label: 'Technical evidence' }
];

const technicalViewerGroups: Array<{ label: string; viewerIds: string[] }> = [
  { label: 'Traces', viewerIds: ['timeline'] },
  { label: 'Runs and operations', viewerIds: ['operations', 'workflow-runs'] },
  { label: 'Packages and artifacts', viewerIds: ['patch-packages', 'patch-artifacts', 'dogfood-receipts'] },
  { label: 'Approvals and policy', viewerIds: ['approval-packages', 'accepted-approvals', 'policy-satisfaction', 'release-readiness-evidence', 'workflow-continuation-evidence'] },
  { label: 'Apply and recovery', viewerIds: ['source-apply-reviews', 'source-apply-dry-run-receipts', 'rollback-evidence'] },
  { label: 'Tools and memory', viewerIds: ['tool-gates', 'memory-proposals'] }
];

function TechnicalEvidence() {
  return (
    <section className="fl-governance__section" data-testid="flow.governance.technical">
      <header><div><h3>Technical evidence</h3><span>Read-only</span></div></header>
      <p className="fl-governance__technical-intro">
        Open preserved evidence viewers when an investigation needs run IDs, traces, packages, receipts, or policy records.
        These links observe existing evidence and perform no governed action.
      </p>
      <div className="fl-governance__technical-groups">
        {technicalViewerGroups.map((group) => (
          <section key={group.label}>
            <h4>{group.label}</h4>
            <div>
              {group.viewerIds.map((id) => {
                const viewer = governanceViewers.find((candidate) => candidate.id === id);
                if (!viewer) return null;
                return (
                  <button key={viewer.id} type="button" onClick={() => navigateProductPath(viewer.entryPath)}>
                    <strong>{viewer.label}</strong>
                    <span>{viewer.disposition}</span>
                  </button>
                );
              })}
            </div>
          </section>
        ))}
      </div>
    </section>
  );
}

function GovernanceSection({ title, count, actionLabel, actionTarget, testId, children }: {
  title: string;
  count: number;
  actionLabel: string;
  actionTarget: string | null;
  testId: string;
  children: React.ReactNode;
}) {
  return (
    <section className="fl-governance__section" data-testid={testId}>
      <header>
        <div><h3>{title}</h3><span>{count}</span></div>
        {actionTarget ? <button type="button" className="fl-governance__text-action" onClick={() => navigateProductPath(actionTarget)}>{actionLabel}</button> : null}
      </header>
      {children}
    </section>
  );
}

function AttentionCard({ item, projectId }: { item: ProjectGovernanceAttentionItem; projectId: number }) {
  const target = safeProductRoute(item.targetRoute, projectId);
  return (
    <article className="fl-governance__attention-card">
      <div className="fl-governance__attention-main">
        <div className="fl-governance__item-heading">
          <strong>{text(item.workItemReference, `WI-${item.workItemId ?? '?'}`)}</strong>
          <StatusBadge status={severityTone(item.severity)}>{severityLabel(item.severity)}</StatusBadge>
        </div>
        <h4>{text(item.title, 'Work Item')}</h4>
        <p>{text(item.summary, 'Human attention is required.')}</p>
        <dl className="fl-governance__item-meta">
          <div><dt>Waiting on</dt><dd>{text(item.waitingOn, 'Project team')}</dd></div>
          <div><dt>Next safe action</dt><dd>{text(item.nextSafeAction, 'Open the Work Item and inspect backend state.')}</dd></div>
          <div><dt>Recorded</dt><dd>{formatTime(item.recordedUtc)}</dd></div>
        </dl>
      </div>
      {target ? <button className="fl-btn" type="button" onClick={() => navigateProductPath(target)}>Open {text(item.workItemReference, 'Work Item')}</button> : null}
    </article>
  );
}

function ExceptionRow({ item, projectId }: { item: ProjectGovernanceException; projectId: number }) {
  const target = safeProductRoute(item.targetRoute, projectId);
  return (
    <article>
      <div className="fl-governance__item-heading"><strong>{text(item.title, 'Governance exception')}</strong><StatusBadge status={severityTone(item.severity)}>{severityLabel(item.severity)}</StatusBadge></div>
      <p>{text(item.summary, 'No summary was returned.')}</p>
      <footer><span>{formatTime(item.recordedUtc)}</span>{target ? <button type="button" className="fl-governance__text-action" onClick={() => navigateProductPath(target)}>Inspect</button> : null}</footer>
    </article>
  );
}

function DecisionRow({ item, projectId }: { item: ProjectGovernanceDecision; projectId: number }) {
  const target = safeProductRoute(item.targetRoute, projectId);
  return (
    <article>
      <strong>{text(item.summary, 'Consequential decision')}</strong>
      <p>{item.actorDisplayName ? `${item.actorDisplayName} · ` : ''}WI-{item.workItemId ?? '?'} · {formatTime(item.recordedUtc)}</p>
      {target ? <button type="button" className="fl-governance__text-action" onClick={() => navigateProductPath(target)}>Open Work Item</button> : null}
    </article>
  );
}

function EmptySection({ children }: { children: React.ReactNode }) {
  return <p className="fl-governance__empty">{children}</p>;
}

function groupControls(controls: ProjectGovernanceControl[]) {
  const groups = new Map<string, ProjectGovernanceControl[]>();
  for (const control of controls) {
    const group = text(control.group, 'Other controls');
    groups.set(group, [...(groups.get(group) ?? []), control]);
  }
  return [...groups.entries()];
}

function governanceSectionTarget(model: ProjectGovernanceOverviewModel, section: GovernanceSection, projectId: number) {
  const backendTarget = section === 'overview'
    ? model.navigation.overview
    : model.navigation[section];
  return safeProductRoute(backendTarget, projectId) ?? governancePath(projectId, section);
}

function detailDescription(section: GovernanceSection) {
  if (section === 'controls') return 'Effective values and their authoritative policy or invariant source.';
  if (section === 'exceptions') return 'Material deviations, degraded controls, and recovery states requiring inspection.';
  if (section === 'decisions') return 'A concise project view of material decisions; Audit owns the complete record.';
  if (section === 'technical') return 'Progressive access to read-only technical evidence and compatibility viewers.';
  return '';
}

function safeProductRoute(value: string | null | undefined, projectId: number): string | null {
  return safeProjectProductPath(value, projectId);
}

function statusTone(status: string | null | undefined): 'ready' | 'warning' | 'danger' | 'neutral' {
  if (status === 'ControlsActive') return 'ready';
  if (status === 'AttentionRequired') return 'warning';
  if (status === 'Degraded') return 'danger';
  return 'neutral';
}

function statusLabel(status: string | null | undefined) {
  if (status === 'ControlsActive') return 'Controls active';
  if (status === 'AttentionRequired') return 'Attention required';
  if (status === 'Degraded') return 'Degraded';
  return 'Unavailable';
}

function severityTone(severity: string | null | undefined): 'danger' | 'warning' | 'info' {
  if (severity === 'Critical') return 'danger';
  if (severity === 'ActionRequired' || severity === 'Warning') return 'warning';
  return 'info';
}

function severityLabel(severity: string | null | undefined) {
  if (severity === 'ActionRequired') return 'Action required';
  return text(severity, 'Information');
}

function formatTime(value: string | null | undefined) {
  if (!value) return 'Time unavailable';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Time unavailable' : parsed.toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' });
}

function text(value: string | null | undefined, fallback: string) {
  return value?.trim() || fallback;
}

function describeError(error: unknown) {
  if (error instanceof IronDevApiError) return error.message;
  return error instanceof Error ? error.message : 'The governance overview could not be loaded.';
}
