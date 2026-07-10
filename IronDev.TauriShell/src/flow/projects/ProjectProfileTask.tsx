import type { ProjectSetupCheckModel } from './projectSetupModel';

interface ProjectProfileTaskProps {
  check: ProjectSetupCheckModel;
  profile: Record<string, unknown> | null;
  busy: boolean;
  errorMessage: string | null;
  onConfirm: () => void;
}

function profileFacts(profile: Record<string, unknown> | null): string[] {
  if (!profile) {
    return [];
  }
  return [profile.applicationType, profile.primaryLanguage, profile.framework, profile.testFramework, profile.solutionFile]
    .filter((value): value is string => typeof value === 'string' && value.trim().length > 0);
}

export function ProjectProfileTask({ check, profile, busy, errorMessage, onConfirm }: ProjectProfileTaskProps) {
  const facts = profileFacts(profile);
  return (
    <>
      <p>Review the detected structure, then confirm that it describes this project.</p>
      <div className="fl-project-setup__profile-summary">
        <strong>Project structure detected</strong>
        {facts.length > 0 ? (
          <ul>
            {facts.map((fact) => (
              <li key={fact}>{fact}</li>
            ))}
          </ul>
        ) : (
          <p>{check.summary}</p>
        )}
      </div>
      {profile ? (
        <details className="fl-project-setup__profile-details">
          <summary>Review detected structure</summary>
          <pre>{JSON.stringify(profile, null, 2)}</pre>
        </details>
      ) : null}
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <div className="fl-project-setup__task-actions">
        <button
          className="fl-btn fl-pri"
          type="button"
          disabled={busy || profile === null}
          data-testid="flow.projectSetup.confirm.ProjectProfile"
          onClick={onConfirm}
        >
          {busy ? 'Saving...' : 'Confirm project structure'}
        </button>
      </div>
    </>
  );
}
