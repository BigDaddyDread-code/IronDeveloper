import type { ProjectSetupCheckModel } from './projectSetupModel';

interface CommandConfirmationTaskProps {
  check: ProjectSetupCheckModel;
  value: string;
  busy: boolean;
  errorMessage: string | null;
  onChange: (value: string) => void;
  onConfirm: () => void;
}

export function CommandConfirmationTask({
  check,
  value,
  busy,
  errorMessage,
  onChange,
  onConfirm
}: CommandConfirmationTaskProps) {
  const isMissing = check.state === 'Missing' && !check.detectedValue;
  const fieldId = `setup-command-${check.code}`;
  const errorId = `${fieldId}-error`;

  return (
    <>
      <p>{isMissing ? 'IronDev could not determine a command for this project.' : 'We detected a likely command but need your confirmation.'}</p>
      <label className="fl-project-setup__field" htmlFor={fieldId}>
        {check.label}
      </label>
      <input
        id={fieldId}
        className="fl-project-setup__command"
        data-testid={`flow.projectSetup.input.${check.code}`}
        value={value}
        aria-invalid={errorMessage !== null}
        aria-describedby={errorMessage ? errorId : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {errorMessage ? (
        <div className="fl-error" id={errorId} role="alert">
          {errorMessage}
        </div>
      ) : null}
      <div className="fl-project-setup__task-actions">
        <button
          className="fl-btn fl-pri"
          type="button"
          disabled={busy || value.trim().length === 0}
          data-testid={`flow.projectSetup.confirm.${check.code}`}
          onClick={onConfirm}
        >
          {busy ? 'Saving...' : isMissing ? `Save ${check.label.toLowerCase()}` : `Confirm ${check.label.toLowerCase()}`}
        </button>
      </div>
    </>
  );
}
