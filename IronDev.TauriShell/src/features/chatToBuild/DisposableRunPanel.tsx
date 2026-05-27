import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { Surface } from '../../design-system/Surface';
import type { StartDisposableCodeRunResponse } from '../../api/types';

interface DisposableRunPanelProps {
  run: StartDisposableCodeRunResponse | null;
  isBusy: boolean;
  disabledReason: string | null;
  onStartRun: () => void;
  onLoadPackage: () => void;
}

export function DisposableRunPanel({ run, isBusy, disabledReason, onStartRun, onLoadPackage }: DisposableRunPanelProps) {
  return (
    <Surface className="chat-build-panel" testId="chat-build.disposableRun">
      <div className="section-heading">
        <p className="eyebrow">Disposable run</p>
        <h2>Backend-owned execution</h2>
      </div>
      {run ? (
        <div className="metadata-stack">
          <MetadataRow label="Run" value={run.runId} />
          <MetadataRow label="State" value={<StatusBadge status={run.state === 'PausedForApproval' ? 'ready' : 'info'}>{run.state}</StatusBadge>} />
          <MetadataRow label="Disposable" value={run.isDisposable ? 'yes' : 'no'} />
        </div>
      ) : (
        <p className="state-muted">Start a disposable code run after ticket review. The UI does not send commands or paths.</p>
      )}
      <div className="chat-build-actions">
        <CommandButton
          type="button"
          variant="primary"
          onClick={onStartRun}
          disabled={isBusy || Boolean(disabledReason)}
          testId="chat-build.command.startDisposableRun"
        >
          {isBusy ? 'Running...' : 'Start Disposable Code Run'}
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
