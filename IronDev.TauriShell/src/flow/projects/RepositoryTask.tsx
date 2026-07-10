import type { ProjectSetupCheckModel } from './projectSetupModel';

interface RepositoryTaskProps {
  check: ProjectSetupCheckModel;
  value: string;
  busy: boolean;
  errorMessage: string | null;
  onChange: (value: string) => void;
  onSave: () => void;
}

function isTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

export function RepositoryTask({ check, value, busy, errorMessage, onChange, onSave }: RepositoryTaskProps) {
  const browse = async () => {
    const dialog = await import('@tauri-apps/plugin-dialog');
    const selected = await dialog.open({ directory: true, multiple: false });
    if (typeof selected === 'string') {
      onChange(selected);
      window.setTimeout(() => document.getElementById('project-setup-repository')?.focus(), 0);
    }
  };

  return (
    <>
      <p>{check.summary || 'IronDev could not safely use this location.'}</p>
      <label className="fl-project-setup__field" htmlFor="project-setup-repository">
        Repository path
      </label>
      <div className="fl-project-setup__repository-row">
        <input
          id="project-setup-repository"
          data-testid="flow.projectSetup.repository.path"
          value={value}
          aria-invalid={errorMessage !== null}
          onChange={(event) => onChange(event.target.value)}
        />
        {isTauriRuntime() ? (
          <button className="fl-btn" type="button" disabled={busy} onClick={() => void browse()}>
            Browse...
          </button>
        ) : null}
      </div>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <div className="fl-project-setup__task-actions">
        <button
          className="fl-btn fl-pri"
          type="button"
          disabled={busy || value.trim().length === 0}
          data-testid="flow.projectSetup.repository.save"
          onClick={onSave}
        >
          {busy ? 'Saving...' : 'Change repository'}
        </button>
      </div>
    </>
  );
}
