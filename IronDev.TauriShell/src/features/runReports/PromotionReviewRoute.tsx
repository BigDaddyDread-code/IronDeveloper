import { useEffect, useMemo } from 'react';
import { useRunReportsWorkspace } from './useRunReportsWorkspace';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { SplitWorkspace } from '../../design-system/SplitWorkspace';
import { Surface } from '../../design-system/Surface';
import { EmptyState } from '../../design-system/EmptyState';
import { WorkspaceListPane } from '../../components/WorkspaceListPane';
import { WorkspaceListItem } from '../../components/WorkspaceListItem';
import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../design-system/StatusBadge';

const defaultFilterLabels: Record<string, string> = {
  latest: 'Latest',
  failed: 'Failed',
  needsHumanReview: 'Needs Human Review',
  promotionCandidate: 'Promotion Candidate'
};

interface PromotionReviewRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function PromotionReviewRoute({ route, onRouteReady }: PromotionReviewRouteProps) {
  const state = useRunReportsWorkspace();

  const promotionReview = state.selectedRun?.promotionReview ?? null;
  const policy = state.selectedRun?.policy ?? null;
  const blockedActions = promotionReview?.blockedActions ?? [];
  const explicitApprovals = promotionReview?.explicitApprovalsNeeded ?? [];

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: `workspace.${route.id}.refresh`,
        label: state.runListStatus === 'loading' ? 'Refreshing runs' : 'Refresh runs',
        intent: 'secondary',
        onExecute: state.actions.onRefreshRuns,
        disabled: state.runListStatus === 'loading',
        busy: state.runListStatus === 'loading',
        testId: 'promotion-review.command.refresh',
        disabledReason: state.runAccessBlocked ?? undefined
      },
      {
        id: `workspace.${route.id}.refreshSelected`,
        label: state.selectedRunStatus === 'loading' ? 'Checking selected run' : 'Refresh selected run',
        intent: 'ghost',
        onExecute: state.actions.onRefreshSelectedRun,
        disabled: state.selectedRunId === null || state.selectedRunStatus === 'loading',
        busy: state.selectedRunStatus === 'loading',
        testId: 'promotion-review.command.refreshSelected',
        disabledReason: state.runAccessBlocked ?? undefined
      }
    ],
    [route.id, state.actions.onRefreshRuns, state.actions.onRefreshSelectedRun, state.runAccessBlocked, state.runListStatus, state.selectedRunId, state.selectedRunStatus]
  );

  const summaryChips = useMemo(
    () => [
      { label: `${state.filteredRuns.length} report(s)` },
      { label: promotionReview ? `Promotion package ${promotionReview.packageId}` : 'No package selected' }
    ],
    [promotionReview?.packageId, state.filteredRuns.length]
  );

  const routeState: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: state.runAccessBlocked,
      workspaceSummaryChips: summaryChips,
      blockReasonTestId: state.runAccessBlocked ? 'promotion-review.blockedReason' : undefined
    }),
    [commands, state.runAccessBlocked, summaryChips]
  );

  useEffect(() => {
    if (onRouteReady) {
      onRouteReady(routeState);
    }
  }, [onRouteReady, routeState]);

  const selectedRunLabel = useMemo(() => {
    if (!state.selectedRun) {
      return 'No run selected';
    }

    const status = state.selectedRun.recommendation ? ` · ${state.selectedRun.recommendation}` : '';
    return `${state.selectedRun.runId ?? 'run'}${status}`;
  }, [state.selectedRun]);

  const nextAction = useMemo(() => {
    if (state.runAccessBlocked) {
      return 'Resolve API and token state before any action.';
    }

    if (!promotionReview) {
      return 'Select a run that includes promotion review details.';
    }

    if ((promotionReview.approvalState ?? '').toLowerCase() !== 'needshumanreview' && promotionReview.approvalState) {
      return `Current approval state is ${promotionReview.approvalState}.`;
    }

    if (!promotionReview.packageId || !promotionReview.proposedChangeId) {
      return 'Promotion identifiers are incomplete. Re-run promotion package creation in source tooling.';
    }

    if ((promotionReview.blockedActions?.length ?? 0) > 0) {
      return 'Address blocked actions before moving toward controlled dry-run.';
    }

    if ((promotionReview.explicitApprovalsNeeded?.length ?? 0) > 0) {
      return 'Create the required approval record before isolated dry-run.';
    }

    return 'Awaiting governed policy-compliant approval path (no direct apply from this UI yet).';
  }, [promotionReview, state.runAccessBlocked]);

  return (
    <main className="promotion-review-workspace" data-testid="promotion-review.workspace">
      <SplitWorkspace
        left={
          <WorkspaceListPane eyebrow="Promotion review" title="Runs to review" testId="promotion-review.list">
            <div className="run-reports-filters" data-testid="promotion-review.filters">
              {state.defaultFilters.map((filter) => (
                <button
                  key={filter}
                  type="button"
                  className={`run-reports-filter ${state.selectedFilter === filter ? 'run-reports-filter--active' : ''}`}
                  data-testid={`promotion-review.filter.${filter}`}
                  onClick={() => state.actions.onSelectFilter(filter)}
                >
                  {defaultFilterLabels[filter]}
                </button>
              ))}
            </div>

            {state.filteredRuns.length === 0 ? (
              <EmptyState title="No matching runs" body="Generate sandbox runs and promotion packages to review them here." />
            ) : (
              <div className="workspace-list-pane__items" data-testid="promotion-review.rows">
                {state.filteredRuns.map((run) => (
                  <WorkspaceListItem
                    key={run.runId ?? `${run.traceId ?? 'run'}-${run.startedUtc}`}
                    testId="promotion-review.row"
                    title={run.title ?? `Run ${run.runId ?? 'unknown'}`}
                    summary={run.recommendation ?? run.status ?? 'No summary available'}
                    isSelected={run.runId === state.selectedRunId}
                    onSelect={() => state.actions.onSelectRun(run.runId ?? null)}
                    badges={
                      <StatusBadge status={run.status?.toLowerCase().includes('fail') ? 'warning' : 'ready'}>{run.status ?? 'unknown'}</StatusBadge>
                    }
                  />
                ))}
              </div>
            )}
          </WorkspaceListPane>
        }
        center={
          <Surface testId="promotion-review.summary">
            <div className="section-heading">
              <p className="eyebrow">Promotion Review</p>
              <h2>{selectedRunLabel}</h2>
            </div>

            {!state.selectedRun ? <EmptyState title="Select a run" body="Choose a run to surface promotable files and review policy context." /> : null}

            {state.selectedRun && !promotionReview ? (
              <EmptyState title="Run review is not available" body="This run report has no promotion-review payload." />
            ) : (
              state.selectedRun &&
              promotionReview && (
                <PromotionReviewPanels
                  run={{
                    project: state.selectedRun?.project,
                    traceId: state.selectedRun?.traceId,
                    realRepoMutationCount: state.selectedRun?.realRepoMutationCount,
                    workspacePath: state.selectedRun?.workspacePath,
                    boundary: state.selectedRun?.boundary,
                    warnings: state.selectedRun?.warnings
                  }}
                  review={promotionReview}
                  policy={policy}
                  nextAction={nextAction}
                />
              )
            )}
          </Surface>
        }
        right={
          <Surface testId="promotion-review.inspector">
            <div className="workflow-section workflow-section--wide">
              <h3>Governance</h3>
              <MetadataRow label="Policy" value={policy?.policyId || 'Default policy in use'} />
              <MetadataRow label="Policy settings" value={policy?.configurableSettings?.join(', ') || 'none'} />
              <MetadataRow
                label="Hard invariants"
                value={
                  <StatusBadge status={policy?.hardInvariants?.length ? 'ready' : 'neutral'}>{policy?.hardInvariants?.length ?? 0}</StatusBadge>
                }
              />
              <MetadataRow label="Required checks" value={promotionReview?.requiredChecks?.join(', ') || 'none'} />
              <MetadataRow label="Explicit approvals" value={explicitApprovals.join(', ') || 'none'} />
            </div>

            <div className="workflow-section workflow-section--wide">
              <h3>Blocked actions</h3>
              {blockedActions.length > 0 ? (
                <ul className="promotion-review-policy-list">
                  {blockedActions.map((action) => (
                    <li key={action}>{action}</li>
                  ))}
                </ul>
              ) : (
                <p className="state-muted" data-testid="promotion-review.blockedActions.empty">
                  No blocked actions returned for this review.
                </p>
              )}
            </div>

            <div className="workflow-section workflow-section--wide">
              <h3>Next allowed action</h3>
              <p className="state-muted" data-testid="promotion-review.nextAction">
                {nextAction}
              </p>
            </div>
          </Surface>
        }
      />
    </main>
  );
}

interface PromotionReviewPanelProps {
  run: {
    project?: string | null;
    traceId?: string | null;
    realRepoMutationCount?: number | null;
    workspacePath?: string | null;
    boundary?: string | null;
    warnings?: string[] | null;
  };
  review: {
    packageId?: string | null;
    proposedChangeId?: string | null;
    approvalState?: string | null;
    recommendation?: string | null;
    runtimeProfileId?: string | null;
    targetLanguage?: string | null;
    targetStack?: string | null;
    promotableFiles?: Array<{ relativePath?: string | null }> | null;
    blockedFiles?: Array<{ relativePath?: string | null }> | null;
    risks?: Array<{ severity?: string | null; category?: string | null; message?: string | null; mitigation?: string | null }> | null;
    requiredChecks?: string[] | null;
    explicitApprovalsNeeded?: string[] | null;
    blockedActions?: string[] | null;
  };
  policy: {
    policyId?: string | null;
    configurableSettings?: string[] | null;
    hardInvariants?: string[] | null;
  } | null;
  nextAction: string;
}

function PromotionReviewPanels({ run, review, policy, nextAction }: PromotionReviewPanelProps) {
  return (
    <div className="promotion-review-panels detail-grid" data-testid="promotion-review.detail">
      <div className="workflow-section">
        <h3>Package context</h3>
        <MetadataRow label="Package id" value={review.packageId || 'Unavailable'} />
        <MetadataRow label="Proposed change id" value={review.proposedChangeId || 'Unavailable'} />
        <MetadataRow label="Approval state" value={review.approvalState || 'Unavailable'} />
        <MetadataRow label="Project" value={run.project || 'Unavailable'} />
        <MetadataRow label="Trace" value={run.traceId || 'Unavailable'} />
        <MetadataRow label="Workspace" value={run.workspacePath || 'Unavailable'} />
        <MetadataRow label="Boundary" value={run.boundary || 'Unavailable'} />
      </div>

      <div className="workflow-section">
        <h3>Runtime / policy snapshot</h3>
        <MetadataRow label="Runtime profile" value={review.runtimeProfileId || 'Unavailable'} />
        <MetadataRow label="Target language" value={review.targetLanguage || 'Unavailable'} />
        <MetadataRow label="Target stack" value={review.targetStack || 'Unavailable'} />
        <MetadataRow
          label="Active repo mutations"
          value={
            <StatusBadge status={run.realRepoMutationCount ? 'warning' : 'ready'}>
              {run.realRepoMutationCount ?? 0}
            </StatusBadge>
          }
        />
        <MetadataRow label="Policy" value={policy?.policyId || 'Default policy in use'} />
      </div>

      <div className="workflow-section">
        <h3>Promotable files ({review.promotableFiles?.length ?? 0})</h3>
        {review.promotableFiles && review.promotableFiles.length > 0 ? (
          <ul className="promotion-review-file-list">
            {review.promotableFiles.map((file) => (
              <li key={file.relativePath || 'promotable-file'}>{file.relativePath}</li>
            ))}
          </ul>
        ) : (
          <p className="state-muted" data-testid="promotion-review.promotableFiles.empty">
            No promotable files.
          </p>
        )}
      </div>

      <div className="workflow-section">
        <h3>Blocked files ({review.blockedFiles?.length ?? 0})</h3>
        {review.blockedFiles && review.blockedFiles.length > 0 ? (
          <ul className="promotion-review-file-list">
            {review.blockedFiles.map((file) => (
              <li key={file.relativePath || 'blocked-file'}>{file.relativePath}</li>
            ))}
          </ul>
        ) : (
          <p className="state-muted" data-testid="promotion-review.blockedFiles.empty">
            No blocked files.
          </p>
        )}
      </div>

      <div className="workflow-section">
        <h3>Hard invariants</h3>
        {policy?.hardInvariants && policy.hardInvariants.length > 0 ? (
          <ul className="promotion-review-policy-list">
            {policy.hardInvariants.map((invariant) => (
              <li key={invariant}>{invariant}</li>
            ))}
          </ul>
        ) : (
          <p className="state-muted">Hard invariants are unavailable in this run payload.</p>
        )}
      </div>

      <div className="workflow-section">
        <h3>Gated actions</h3>
        {review.blockedActions && review.blockedActions.length > 0 ? (
          <ul className="promotion-review-policy-list">
            {review.blockedActions.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        ) : (
          <p className="state-muted">No blocked actions enumerated.</p>
        )}
      </div>

      <div className="workflow-section">
        <h3>Allowed review guidance</h3>
        <p className="state-muted">{nextAction}</p>
      </div>
    </div>
  );
}

