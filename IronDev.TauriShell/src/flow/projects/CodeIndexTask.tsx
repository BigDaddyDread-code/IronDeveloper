import type { ProjectSetupCheckModel } from './projectSetupModel';

interface CodeIndexTaskProps {
  check: ProjectSetupCheckModel;
  busy: boolean;
  errorMessage: string | null;
  onIndex: () => void;
}

export function CodeIndexTask({ check, busy, errorMessage, onIndex }: CodeIndexTaskProps) {
  return (
    <>
      <p>
        IronDev has detected the repository, but it must index the configured source tree before a governed run can start.
      </p>
      <p className="fl-project-setup__remedy">{check.summary}</p>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <div className="fl-project-setup__task-actions">
        <button
          className="fl-btn fl-pri"
          type="button"
          data-testid="flow.projectSetup.indexProject"
          disabled={busy}
          onClick={onIndex}
        >
          {busy ? 'Indexing project...' : 'Index project'}
        </button>
      </div>
    </>
  );
}
