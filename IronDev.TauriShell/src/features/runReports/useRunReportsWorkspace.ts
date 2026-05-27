import { useCallback, useEffect, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ApiStatus,
  RunEvidenceItem,
  RunReportDetail,
  RunReportSummary,
  RunReportsLoadStatus
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { useWorkspaceNavigation } from '../../state/useWorkspaceNavigation';

export type RunReportsFilter = 'latest' | 'failed' | 'needsHumanReview' | 'promotionCandidate';

interface RunReportsWorkspaceState {
  runs: RunReportSummary[];
  filteredRuns: RunReportSummary[];
  selectedRunId: string | null;
  selectedRun: RunReportDetail | null;
  runListStatus: RunReportsLoadStatus;
  runListMessage: string;
  selectedRunStatus: RunReportsLoadStatus;
  selectedRunMessage: string;
  runEvidence: RunEvidenceItem[];
  selectedFilter: RunReportsFilter;
  runAccessBlocked: string | null;
}

interface RunReportsWorkspaceActions {
  onSelectRun: (runId: string | null) => void;
  onRefreshRuns: () => void;
  onSelectFilter: (next: RunReportsFilter) => void;
  onRefreshSelectedRun: () => void;
}

export interface RunReportsWorkspaceViewModel extends RunReportsWorkspaceState {
  actions: RunReportsWorkspaceActions;
}

const defaultLoadStatusMessage = {
  list: 'Checking for run reports.',
  unavailable: 'Select a project and connect the API before loading run reports.',
  authRequired: 'Sign in or provide a token before loading run reports.',
  apiOffline: 'IronDev.Api is offline for run report loading.'
};

const defaultFilters: RunReportsFilter[] = ['latest', 'failed', 'needsHumanReview', 'promotionCandidate'];

export function useRunReportsWorkspace() {
  const session = useSessionContext();
  const project = useProjectContext();
  const navigation = useWorkspaceNavigation();
  const [runs, setRuns] = useState<RunReportSummary[]>([]);
  const selectedRunId = navigation.selectedRunId;
  const [selectedRun, setSelectedRun] = useState<RunReportDetail | null>(null);
  const [runListStatus, setRunListStatus] = useState<RunReportsLoadStatus>('idle');
  const [runListMessage, setRunListMessage] = useState(defaultLoadStatusMessage.list);
  const [selectedRunStatus, setSelectedRunStatus] = useState<RunReportsLoadStatus>('idle');
  const [selectedRunMessage, setSelectedRunMessage] = useState('Load a run to inspect evidence and promotion context.');
  const [runEvidence, setRunEvidence] = useState<RunEvidenceItem[]>([]);
  const [selectedFilter, setSelectedFilter] = useState<RunReportsFilter>('latest');

  const tokenConfigured = session.tokenConfigured;
  const runAccessBlocked = getRunReportsBlocker(session.apiStatus, tokenConfigured);

  const selectedRunDetails = useMemo(() => runs.find((run) => run.runId === selectedRunId) ?? null, [runs, selectedRunId]);
  const selectedRunLabel = selectedRunDetails?.title ?? selectedRunDetails?.runId ?? 'selected run';

  const filteredRuns = useMemo(() => getFilteredRuns(runs, selectedFilter), [runs, selectedFilter]);

  const loadRuns = useCallback(async () => {
    if (runAccessBlocked) {
      setRunListMessage(runAccessBlocked);
      setRunListStatus('unavailable');
      setRuns([]);
      navigation.setSelectedRunId(null);
      return;
    }

    setRunListStatus('loading');
    setRunListMessage('Loading recent run reports...');

    const controller = new AbortController();

    try {
      const response = await session.client.getRunReports(controller.signal);
      const sortedRuns = [...response].sort((left, right) => {
        const leftTime = Date.parse(left.startedUtc ?? '') || Number.NEGATIVE_INFINITY;
        const rightTime = Date.parse(right.startedUtc ?? '') || Number.NEGATIVE_INFINITY;

        return rightTime - leftTime;
      });

      if (controller.signal.aborted) {
        return;
      }

      setRuns(sortedRuns);
      setRunListStatus('loaded');
      setRunListMessage(
        sortedRuns.length === 0
          ? 'No run reports yet. Trigger a run to begin collecting evidence.'
          : `Loaded ${sortedRuns.length} run report(s).`
      );

      const nextRunId =
        selectedRunId && sortedRuns.some((run) => run.runId === selectedRunId) ? selectedRunId : sortedRuns[0]?.runId ?? null;
      navigation.setSelectedRunId(nextRunId);
    } catch (error) {
      if (controller.signal.aborted) {
        return;
      }

      setRunListStatus('error');
      navigation.setSelectedRunId(null);
      setSelectedRun(null);
      setRunEvidence([]);

      if (error instanceof IronDevApiError && error.isAuthFailure) {
        setRunListMessage('Run reports request was rejected by API auth. Sign in and retry.');
      } else if (error instanceof IronDevApiError) {
        setRunListMessage(`Run reports request failed with HTTP ${error.status}.`);
      } else {
        setRunListMessage('Run report list could not reach IronDev.Api.');
      }
    } finally {
      controller.abort();
    }
  }, [navigation, runAccessBlocked, selectedRunId, session.client]);

  const loadSelectedRun = useCallback(
    async (runId: string | null) => {
      if (!runId) {
        setSelectedRun(null);
        setRunEvidence([]);
        setSelectedRunStatus('unavailable');
        setSelectedRunMessage('Select a run to view report details and evidence.');
        return;
      }

      if (runAccessBlocked) {
        setSelectedRun(null);
        setRunEvidence([]);
        setSelectedRunStatus('unavailable');
        setSelectedRunMessage(runAccessBlocked);
        return;
      }

      setSelectedRunStatus('loading');
      setSelectedRunMessage(`Loading run ${runId} report...`);

      const controller = new AbortController();

      try {
        const [report, evidence] = await Promise.all([
          session.client.getRunReport(runId, controller.signal),
          session.client.getRunReportEvidence(runId, controller.signal)
        ]);

        if (controller.signal.aborted) {
          return;
        }

        setSelectedRun(report);
        setRunEvidence(Array.isArray(evidence) ? evidence : []);
        setSelectedRunStatus('loaded');
        setSelectedRunMessage(report.summary ?? `${runId} report loaded.`);
      } catch (error) {
        if (controller.signal.aborted) {
          return;
        }

        setSelectedRunStatus('error');
        setSelectedRun(null);
        setRunEvidence([]);
        setSelectedRunMessage(
          error instanceof IronDevApiError ? `Run detail failed with HTTP ${error.status}.` : 'Could not load run report details.'
        );
      } finally {
        controller.abort();
      }
    },
    [runAccessBlocked, session.client]
  );

  useEffect(() => {
    void loadRuns();
  }, [loadRuns, session.config.apiBaseUrl, session.config.token, project.selectedProjectId]);

  useEffect(() => {
    void loadSelectedRun(selectedRunId);
  }, [loadSelectedRun, selectedRunId]);

  const actions: RunReportsWorkspaceActions = useMemo(
    () => ({
      onSelectRun: (runId) => {
        navigation.setSelectedRunId(runId);
      },
      onRefreshRuns: () => void loadRuns(),
      onSelectFilter: (next) => {
        setSelectedFilter(next);
        const filteredSelectedRunId = navigation.selectedRunId;
        const nextRuns = getFilteredRuns(runs, next);
        const nextRunId = filteredSelectedRunId && nextRuns.some((run) => run.runId === filteredSelectedRunId) ? filteredSelectedRunId : nextRuns[0]?.runId ?? null;
        navigation.setSelectedRunId(nextRunId);
      },
      onRefreshSelectedRun: () => void loadSelectedRun(selectedRunId)
    }),
    [loadRuns, loadSelectedRun, navigation, runs, selectedRunId]
  );

  const state: RunReportsWorkspaceState = {
    runs,
    filteredRuns,
    selectedRunId,
    selectedRun,
    runListStatus,
    runListMessage,
    selectedRunStatus,
    selectedRunMessage,
    runEvidence,
    selectedFilter,
    runAccessBlocked
  };

  return {
    ...state,
    actions,
    selectedRunLabel,
    defaultFilters
  };
}

function getFilteredRuns(runs: RunReportSummary[], filter: RunReportsFilter) {
  const normalized = [...runs].sort((left, right) => {
    const leftTime = Date.parse(left.startedUtc ?? left.completedUtc ?? '') || 0;
    const rightTime = Date.parse(right.startedUtc ?? right.completedUtc ?? '') || 0;

    return rightTime - leftTime;
  });

  switch (filter) {
    case 'failed':
      return normalized.filter((run) => isFailedRun(run));
    case 'needsHumanReview':
      return normalized.filter((run) => needsHumanReview(run));
    case 'promotionCandidate':
      return normalized.filter((run) => isPromotionCandidate(run));
    default:
      return normalized;
  }
}

export function isFailedRun(run: RunReportSummary) {
  const value = `${run.status ?? ''} ${run.recommendation ?? ''}`.toLowerCase();

  return value.includes('failed') || value.includes('error') || value.includes('warning');
}

export function needsHumanReview(run: RunReportSummary) {
  const value = `${run.status ?? ''} ${run.recommendation ?? ''}`.toLowerCase();
  return value.includes('human') || value.includes('review');
}

export function isPromotionCandidate(run: RunReportSummary) {
  const value = `${run.status ?? ''} ${run.recommendation ?? ''}`.toLowerCase();
  return value.includes('promotion') || value.includes('promotable');
}

export interface RunReportsWorkspaceContext {
  runs: RunReportSummary[];
  filteredRuns: RunReportSummary[];
  selectedRun: RunReportDetail | null;
  selectedRunId: string | null;
  selectedRunLabel: string;
  selectedRunStatus: RunReportsLoadStatus;
  selectedRunMessage: string;
  runListStatus: RunReportsLoadStatus;
  runListMessage: string;
  runEvidence: RunEvidenceItem[];
  selectedFilter: RunReportsFilter;
  runAccessBlocked: string | null;
  defaultFilters: RunReportsFilter[];
}

function getRunReportsBlocker(apiStatus: ApiStatus, tokenConfigured: boolean) {
  if (apiStatus.status === 'disconnected') {
    return 'IronDev.Api is offline. Start the backend before loading run reports.';
  }

  if (apiStatus.status !== 'connected') {
    return 'IronDev.Api is not ready yet. Retry connection before loading run reports.';
  }

  if (!tokenConfigured) {
    return 'Sign in with a valid token before loading run reports.';
  }

  return null;
}

export type { ApiStatus as RunSessionApiStatus };
