import { useEffect, useMemo } from 'react';
import { useRunReportsWorkspace } from './useRunReportsWorkspace';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import type { RunReportDetail, RunReportSummary } from '../../api/types';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import { SplitWorkspace } from '../../design-system/SplitWorkspace';
import { Surface } from '../../design-system/Surface';
import { EmptyState } from '../../design-system/EmptyState';
import { WorkspaceListPane } from '../../components/WorkspaceListPane';
import { WorkspaceListItem } from '../../components/WorkspaceListItem';
import { MetadataRow } from '../../components/MetadataRow';
import { InspectorSection } from '../../components/InspectorSection';
import { EvidenceCard } from '../../components/EvidenceCard';
import { StatusBadge } from '../../design-system/StatusBadge';

interface RunReportsRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const defaultFilterLabels: Record<string, string> = {
  latest: 'Latest',
  failed: 'Failed',
  needsHumanReview: 'Needs Human Review',
  promotionCandidate: 'Promotion Candidate'
};

export function RunReportsRoute({ route, onRouteReady }: RunReportsRouteProps) {
  const state = useRunReportsWorkspace();

  const selectedRun = state.selectedRun;
  const selectedPolicy = selectedRun?.policy ?? null;
  const selectedTrace = selectedRun?.traceId ?? null;
  const selectedHardInvariants = selectedPolicy?.hardInvariants ?? [];
  const selectedBlockedActions = selectedRun?.promotionReview?.blockedActions ?? [];
  const selectedRecommendation = selectedRun?.recommendation ?? selectedRun?.summary ?? 'No recommendation recorded';
  const selectedPolicyId = selectedPolicy?.policyId ?? 'Unavailable';

  const nextAction = useMemo(() => {
    if (state.runAccessBlocked) {
      return 'Resolve API and authentication state before acting on runs.';
    }

    if (!selectedRun) {
      return 'Select a run to inspect governance and decide the next action.';
    }

    if (selectedRun.status && selectedRun.status.toLowerCase().includes('fail')) {
      return 'Run failed. Review timeline and evidence before any further automation.';
    }

    if (selectedBlockedActions.length > 0) {
      return 'Address blocked actions before attempting controlled write dry-run.';
    }

    if (selectedRun.promotionReview?.explicitApprovalsNeeded?.length) {
      return `Acquire required approvals: ${selectedRun.promotionReview.explicitApprovalsNeeded.join(', ')}.`;
    }

    if (selectedPolicy?.hardInvariants && selectedPolicy.hardInvariants.length > 2) {
      return 'Policy invariants are strict. Confirm the run can satisfy governance before continuing.';
    }

    return 'No explicit blocks surfaced; continue with governance-guided promotion review.';
  }, [selectedBlockedActions.length, selectedPolicy?.hardInvariants, selectedRun, state.runAccessBlocked]);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: `workspace.${route.id}.refresh`,
        label: state.runListStatus === 'loading' ? 'Refreshing runs' : 'Refresh runs',
        intent: 'secondary',
        onExecute: state.actions.onRefreshRuns,
        disabled: state.runListStatus === 'loading',
        busy: state.runListStatus === 'loading',
        testId: 'run-reports.command.refresh',
        disabledReason: state.runAccessBlocked ?? undefined
      },
      {
        id: `workspace.${route.id}.refreshSelected`,
        label: state.selectedRunStatus === 'loading' ? 'Refreshing selected run' : 'Refresh selected run',
        intent: 'ghost',
        onExecute: state.actions.onRefreshSelectedRun,
        disabled: state.selectedRunId === null || state.selectedRunStatus === 'loading',
        busy: state.selectedRunStatus === 'loading',
        testId: 'run-reports.command.refreshSelected',
        disabledReason: state.runAccessBlocked ?? undefined
      }
    ],
    [route.id, state.actions.onRefreshRuns, state.actions.onRefreshSelectedRun, state.runAccessBlocked, state.runListStatus, state.selectedRunId, state.selectedRunStatus]
  );

  const summaryChips = useMemo(() => {
    const chips = [`${state.filteredRuns.length} run(s)`];

    if (selectedRun?.runId) {
      chips.push(`Trace ${selectedRun.traceId ?? 'Unavailable'}`);
      chips.push(`Policy ${selectedPolicyId}`);
    }

    return chips.map((label) => ({ label }));
  }, [selectedPolicyId, selectedRun?.runId, selectedRun?.traceId, state.filteredRuns.length]);

  const routeState: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: commands,
      workspaceBlockReason: state.runAccessBlocked,
      workspaceSummaryChips: summaryChips,
      blockReasonTestId: state.runAccessBlocked ? 'run-reports.blockedReason' : undefined
    }),
    [commands, state.runAccessBlocked, summaryChips]
  );

  useEffect(() => {
    if (onRouteReady) {
      onRouteReady(routeState);
    }
  }, [onRouteReady, routeState]);

  return (
    <main className="run-reports-workspace" data-testid="run-reports.workspace">
      <SplitWorkspace
        left={
          <WorkspaceListPane eyebrow="Runs" title="Execution history" testId="run-reports.list">
            <div className="run-reports-filters" data-testid="run-reports.filters">
              {state.defaultFilters.map((filter) => (
                <button
                  key={filter}
                  type="button"
                  className={`run-reports-filter ${state.selectedFilter === filter ? 'run-reports-filter--active' : ''}`}
                  data-testid={`run-reports.filter.${filter}`}
                  onClick={() => state.actions.onSelectFilter(filter)}
                >
                  {defaultFilterLabels[filter]}
                </button>
              ))}
            </div>

            {state.runListStatus === 'loading' ? (
              <EmptyState title="Loading runs" body={state.runListMessage} />
            ) : state.filteredRuns.length === 0 ? (
              <EmptyState
                title={state.runListStatus === 'error' ? 'Runs unavailable' : 'No runs match this filter'}
                body={state.runListMessage}
              />
            ) : (
              <div className="workspace-list-pane__items" data-testid="run-reports.rows">
                {state.filteredRuns.map((run, index) => (
                  <RunReportListItem
                    key={run.runId ?? `${run.traceId ?? 'run'}-${run.startedUtc ?? index}`}
                    run={run}
                    isSelected={run.runId === state.selectedRunId}
                    onSelect={() => state.actions.onSelectRun(run.runId ?? null)}
                  />
                ))}
              </div>
            )}
          </WorkspaceListPane>
        }
        center={
          <Surface testId="run-reports.summary">
            <div className="section-heading">
              <p className="eyebrow">Run report</p>
              <h2>{selectedRun?.runId ?? 'No run selected'}</h2>
            </div>

            {state.selectedRunStatus === 'loading' ? (
              <EmptyState title="Loading run detail" body={state.selectedRunMessage} />
            ) : state.selectedRunId === null ? (
              <EmptyState title="Select a run" body={state.selectedRunMessage} />
            ) : state.runAccessBlocked ? (
              <EmptyState title="Run reports blocked" body={state.runAccessBlocked} />
            ) : (
              <RunReportSummaryPanel run={state.selectedRun} status={state.selectedRunStatus} message={state.selectedRunMessage} policy={selectedPolicy} />
            )}
          </Surface>
        }
        right={
          <Surface testId="run-reports.inspector">
            <InspectorSection title="Context trace" testId="run-reports.governance">
              <MetadataRow label="Trace" value={selectedTrace ?? 'Unavailable'} />
              <MetadataRow label="Recommendation" value={selectedRecommendation} />
              <MetadataRow label="Policy snapshot" value={selectedPolicyId} />
              <MetadataRow label="Project" value={selectedRun?.project ?? 'Unavailable'} />
              <MetadataRow label="Workspace" value={selectedRun?.workspacePath ?? 'Unavailable'} />
              <MetadataRow label="Boundary" value={selectedRun?.boundary ?? 'Unavailable'} />
            </InspectorSection>

            <InspectorSection title="Hard invariants" testId="run-reports.invariants">
              <EvidenceCard title="Policy constraints">
                {selectedHardInvariants.length > 0 ? (
                  <ul className="run-reports-policy-list">
                    {selectedHardInvariants.map((item) => (
                      <li key={item}>{item}</li>
                    ))}
                  </ul>
                ) : (
                  <p className="state-muted">No hard invariants were returned for this run.</p>
                )}
              </EvidenceCard>
            </InspectorSection>

            <InspectorSection title="Blocked actions" testId="run-reports.blockedActions">
              {selectedBlockedActions.length > 0 ? (
                <ul className="run-reports-policy-list">
                  {selectedBlockedActions.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              ) : (
                <p className="state-muted">No blocked actions were returned for this run.</p>
              )}
            </InspectorSection>

            <InspectorSection title="Evidence files" testId="run-reports.evidence">
              {state.runEvidence.length === 0 ? (
                <p className="state-muted">Evidence is not available for this run.</p>
              ) : (
                <ul className="run-reports-evidence-list">
                  {state.runEvidence.map((evidenceItem) => (
                    <li key={`${evidenceItem.path}-${evidenceItem.summary}`}>
                      <strong>{evidenceItem.type ?? 'File'}</strong> <code>{evidenceItem.path}</code>
                      {evidenceItem.summary ? ` - ${evidenceItem.summary}` : ''}
                    </li>
                  ))}
                </ul>
              )}
            </InspectorSection>

            <InspectorSection title="Next allowed action" testId="run-reports.nextAction">
              <p className="state-muted">{nextAction}</p>
            </InspectorSection>
          </Surface>
        }
      />
    </main>
  );
}

function RunReportListItem({
  run,
  isSelected,
  onSelect
}: {
  run: RunReportSummary;
  isSelected: boolean;
  onSelect: () => void;
}) {
  const label = run.title ?? `Run ${run.runId ?? 'unknown'}`;
  const summary = run.recommendation ?? run.status ?? 'No summary available';

  return (
    <WorkspaceListItem
      testId="run-reports.row"
      title={label}
      summary={`${run.project ?? 'No project'} - ${summary}`}
      isSelected={isSelected}
      onSelect={onSelect}
      badges={
        <>
          <StatusBadge status="info">{run.status ?? 'unknown'}</StatusBadge>
          <StatusBadge status={run.status?.toLowerCase().includes('failed') ? 'warning' : 'neutral'}>
            Mutations {run.realRepoMutationCount ?? 0}
          </StatusBadge>
        </>
      }
    />
  );
}

function RunReportSummaryPanel({
  run,
  status,
  message,
  policy
}: {
  run: RunReportDetail | null;
  status: string;
  message: string;
  policy: { policyId?: string | null; configurableSettings?: string[] | null } | null;
}) {
  if (!run) {
    return <EmptyState title="Select a run to inspect evidence." body={message} />;
  }

  return (
    <div className="detail-grid">
      <div className="workflow-section workflow-section--wide">
        <h3>Summary</h3>
        <MetadataRow label="Run" value={run.runId ?? 'Unavailable'} />
        <MetadataRow label="Trace" value={run.traceId ?? 'Unavailable'} />
        <MetadataRow label="Project" value={run.project ?? 'Unavailable'} />
        <MetadataRow label="Workspace" value={run.workspacePath ?? 'Unavailable'} />
        <MetadataRow label="Recommendation" value={run.recommendation ?? 'Unavailable'} />
        <MetadataRow
          label="Status"
          value={
            <StatusBadge status={run.status?.toLowerCase().includes('fail') ? 'warning' : 'ready'}>
              {run.status ?? 'unknown'}
            </StatusBadge>
          }
        />
      </div>

        <div className="workflow-section workflow-section--wide">
        <h3>Signals</h3>
        <RunReportTimeline run={run} />
        <MetadataRow label="Mutations" value={`${run.realRepoMutationCount ?? 0} real, ${run.disposableFilesChanged ?? 0} sandbox`} />
        <MetadataRow label="Policy profile" value={policy?.policyId ?? run.status ?? 'Default policy'} />
        <MetadataRow
          label="Policy settings"
          value={policy?.configurableSettings && policy.configurableSettings.length > 0 ? policy.configurableSettings.join(', ') : 'none'}
        />
        <MetadataRow label="Started" value={DateTimeDisplay.toLocalDisplay((run as { startedUtc?: string | null }).startedUtc)} />
        <MetadataRow label="Completed" value={DateTimeDisplay.toLocalDisplay((run as { completedUtc?: string | null }).completedUtc)} />
        <MetadataRow
          label="Notes"
          value={
            <span className="state-muted">
              {status === 'error' ? `Unable to load full signal for this run: ${message}` : run.summary ?? 'No run summary'}
            </span>
          }
        />
      </div>
    </div>
  );
}

function RunReportTimeline({ run }: { run: RunReportDetail }) {
  const timeline = buildRunTimelineEntries(run);

  if (timeline.length === 0) {
    return <p className="state-muted">No timeline data was returned for this run.</p>;
  }

  return (
    <ul className="run-report-timeline" data-testid="run-reports.timeline">
      {timeline.map((item) => (
        <li className="run-report-timeline__item" key={item.id}>
          <MetadataRow label={item.group} value={item.label} />
          <MetadataRow label="Status" value={item.status} />
          {item.details ? <MetadataRow label="Details" value={item.details} /> : null}
        </li>
      ))}
    </ul>
  );
}

function buildRunTimelineEntries(
  run: RunReportDetail
): Array<{ id: string; group: string; label: string; status: string; details: string | null }> {
  const stageEntries = (run.stages ?? []).map((stage, index) => ({
    id: `stage-${index}-${stage.stageName ?? 'stage'}`,
    group: 'Stage',
    label: stage.stageName ?? 'Unnamed stage',
    status: stage.status ?? 'Unknown',
    details: stage.agentName ? `Agent ${stage.agentName}` : null
  }));

  const attemptEntries = (run.attempts ?? []).map((attempt, index) => ({
    id: `attempt-${index}-${attempt.type ?? 'attempt'}`,
    group: `Attempt ${attempt.attemptNumber ?? index + 1}`,
    label: attempt.type ?? 'Unknown attempt',
    status: attempt.status ?? 'Unknown',
    details: attempt.summary ?? attempt.failureClassification ?? null
  }));

  const repairEntries = (run.repairs ?? []).map((repair, index) => ({
    id: `repair-${index}-${repair.plannedFix ?? 'repair'}`,
    group: 'Repair',
    label: repair.plannedFix ?? 'Planned repair',
    status: repair.status ?? 'Unknown',
    details:
      [repair.triggerFailureClassification, repair.retryBudgetRemaining != null ? `Retry budget ${repair.retryBudgetRemaining}` : null]
        .filter(Boolean)
        .join(' / ') || null
  }));

  return [...stageEntries, ...attemptEntries, ...repairEntries];
}

