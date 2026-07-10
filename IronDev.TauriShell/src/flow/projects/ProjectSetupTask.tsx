import { CommandConfirmationTask } from './CommandConfirmationTask';
import { ProjectProfileTask } from './ProjectProfileTask';
import { RepositoryTask } from './RepositoryTask';
import { setupActionKinds, type ProjectSetupModel } from './projectSetupModel';

interface ProjectSetupTaskProps {
  model: ProjectSetupModel;
  commandValue: string;
  repositoryValue: string;
  busy: boolean;
  errorMessage: string | null;
  onCommandChange: (value: string) => void;
  onConfirmCommand: () => void;
  onConfirmProfile: () => void;
  onRepositoryChange: (value: string) => void;
  onSaveRepository: () => void;
  onRecheck: () => void;
}

function taskTitle(kind: string): string {
  switch (kind) {
    case setupActionKinds.changeRepository:
      return 'Choose a usable repository';
    case setupActionKinds.confirmBuildCommand:
      return 'Confirm the build command';
    case setupActionKinds.confirmTestCommand:
      return 'Confirm the test command';
    case setupActionKinds.confirmProjectProfile:
      return 'Confirm project structure';
    case setupActionKinds.recheckSetup:
      return 'Re-check project setup';
    default:
      return 'Additional setup required';
  }
}

export function ProjectSetupTask(props: ProjectSetupTaskProps) {
  const { model, busy, errorMessage } = props;
  const check = model.currentCheck;
  const kind = model.nextAction.kind;

  return (
    <>
      <p className="fl-plabel">Next step</p>
      <h2 id="project-setup-task-title">{taskTitle(kind)}</h2>
      {check === null ? (
        <>
          <p>{model.nextAction.nextSafeAction || 'The backend did not provide a recognised setup task.'}</p>
          {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
        </>
      ) : kind === setupActionKinds.changeRepository ? (
        <RepositoryTask
          check={check}
          value={props.repositoryValue}
          busy={busy}
          errorMessage={errorMessage}
          onChange={props.onRepositoryChange}
          onSave={props.onSaveRepository}
        />
      ) : kind === setupActionKinds.confirmBuildCommand || kind === setupActionKinds.confirmTestCommand ? (
        <CommandConfirmationTask
          check={check}
          value={props.commandValue}
          busy={busy}
          errorMessage={errorMessage}
          onChange={props.onCommandChange}
          onConfirm={props.onConfirmCommand}
        />
      ) : kind === setupActionKinds.confirmProjectProfile ? (
        <ProjectProfileTask
          check={check}
          profile={model.source.proposedProfile ?? null}
          busy={busy}
          errorMessage={errorMessage}
          onConfirm={props.onConfirmProfile}
        />
      ) : kind === setupActionKinds.recheckSetup ? (
        <>
          <p>{check.summary}</p>
          <p className="fl-project-setup__remedy">Next step: {check.remedy}</p>
          {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
          <div className="fl-project-setup__task-actions">
            <button className="fl-btn fl-pri" type="button" disabled={busy} onClick={props.onRecheck}>
              {busy ? 'Checking...' : 'Re-check setup'}
            </button>
          </div>
        </>
      ) : (
        <>
          <p>{check.summary}</p>
          <p className="fl-project-setup__remedy">Next step: {check.remedy || model.nextAction.nextSafeAction}</p>
          {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
        </>
      )}
    </>
  );
}
