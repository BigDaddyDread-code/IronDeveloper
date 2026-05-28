import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { Surface } from '../../design-system/Surface';
import type { RunEventSummary, StartDisposableCodeRunResponse } from '../../api/types';
import { RunEventTimeline } from './RunEventTimeline';

interface DisposableRunPanelProps {
  run: StartDisposableCodeRunResponse | null;
  events: RunEventSummary[];
  isBusy: boolean;
  disabledReason: string | null;
  onStartRun: () => void;
  onLoadPackage: () => void;
}

export function DisposableRunPanel({
  run,
  events,
  isBusy,
  disabledReason,
  onStartRun,
  onLoadPackage
}: DisposableRunPanelProps) {
  const currentEvent = events.at(-1);

  return (
    <Surface className="chat-build-panel chat-build-panel--run" testId="chat-build.disposableRun">
      <div className="section-heading">
        <p className="eyebrow">Sandbox run</p>
        <h2>Review-only execution</h2>
      </div>
      {run ? (
        <div className="chat-build-artifact">
          <div className="metadata-stack">
            <MetadataRow label="Run" value={run.runId} />
            <MetadataRow label="State" value={<StatusBadge status={run.state === 'PausedForApproval' ? 'ready' : run.state === 'Failed' ? 'danger' : 'info'}>{run.state}</StatusBadge>} />
            <MetadataRow label="Sandboxed" value={run.isDisposable ? 'yes' : 'no'} />
            <MetadataRow label="Current event" value={currentEvent ? currentEvent.eventType : 'Review package events pending'} />
          </div>
          <p className="chat-build-safety-note">Generated code is held in sandbox evidence. This surface does not apply it to the real repository.</p>
          <RunEventTimeline events={events} compact testId="chat-build.disposableRunTimeline" />
        </div>
      ) : (
        <p className="state-muted">Start a sandbox code run after ticket review. The UI does not send commands or paths.</p>
      )}
      <div className="chat-build-actions">
        <CommandButton
          type="button"
          variant="primary"
          onClick={onStartRun}
          disabled={isBusy || Boolean(disabledReason)}
          testId="chat-build.command.startDisposableRun"
        >
          {isBusy ? 'Running...' : 'Start Sandbox Code Run'}
        </CommandButton>
        <CommandButton
          type="button"
          variant="secondary"
          onClick={onLoadPackage}
          disabled={isBusy || !run}
          testId="chat-build.command.loadReviewPackage"
        >
          Load Review Package
        </CommandButton>
        {disabledReason ? <p className="state-muted">{disabledReason}</p> : null}
      </div>
    </Surface>
  );
}
