import { useState } from 'react';

interface BuilderWorkspacePermissionTaskProps {
  busy: boolean;
  errorMessage: string | null;
  onEnable: () => void;
}

export function BuilderWorkspacePermissionTask({
  busy,
  errorMessage,
  onEnable
}: BuilderWorkspacePermissionTaskProps) {
  const [confirmed, setConfirmed] = useState(false);

  return (
    <>
      <p>This allows the Builder to write only inside IronDev-controlled disposable workspaces.</p>
      <div className="fl-project-setup__safety-boundary">
        <p>It does not approve changes.</p>
        <p>It does not apply to the source repository.</p>
        <p>It does not commit, push, merge, release or deploy.</p>
      </div>
      <label className="fl-project-setup__confirmation">
        <input
          type="checkbox"
          data-testid="flow.projectSetup.confirmBuilderBoundary"
          checked={confirmed}
          disabled={busy}
          onChange={(event) => setConfirmed(event.currentTarget.checked)}
        />
        I understand these limits and want to enable governed Builder workspace writes.
      </label>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <div className="fl-project-setup__task-actions">
        <button
          className="fl-btn fl-pri"
          type="button"
          data-testid="flow.projectSetup.enableBuilderApply"
          disabled={busy || !confirmed}
          onClick={onEnable}
        >
          {busy ? 'Enabling governed Builder writes...' : 'Enable governed Builder writes'}
        </button>
      </div>
    </>
  );
}
