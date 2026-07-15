import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ProjectProvisioningReadinessUi } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { ProjectSetupChecklist } from './ProjectSetupChecklist';
import { ProjectSetupDetails } from './ProjectSetupDetails';
import { ProjectSetupHeader } from './ProjectSetupHeader';
import { ProjectSetupTask } from './ProjectSetupTask';
import { adaptProjectSetup } from './projectSetupAdapter';

interface ProjectSetupScreenProps {
  projectId: number;
  entryMode?: boolean;
  onBackToProjects: () => void;
  onOpenBoard: () => void;
}

type LoadState = 'loading' | 'loaded' | 'unavailable';

function describeError(error: unknown, fallback: string): string {
  if (error instanceof IronDevApiError && error.body && typeof error.body === 'object') {
    const body = error.body as { message?: unknown; error?: unknown; nextSafeActions?: unknown };
    const backendReason = typeof body.message === 'string'
      ? body.message
      : typeof body.error === 'string'
        ? body.error
        : null;
    if (backendReason) {
      const nextSafeAction = Array.isArray(body.nextSafeActions) && typeof body.nextSafeActions[0] === 'string'
        ? body.nextSafeActions[0]
        : null;
      return nextSafeAction ? `${backendReason} ${nextSafeAction}` : backendReason;
    }
  }
  return error instanceof Error && error.message.trim().length > 0 ? error.message : fallback;
}

export function ProjectSetupScreen({
  projectId,
  entryMode = false,
  onBackToProjects,
  onOpenBoard
}: ProjectSetupScreenProps) {
  const session = useSessionContext();
  const projects = useProjectContext();
  const selectedProject = projects.projects.find((candidate) => candidate.id === projectId);
  const projectName = selectedProject?.name ?? projects.selectedProjectName ?? `Project ${projectId}`;
  const projectPath = selectedProject?.localPath ?? '';
  const [readiness, setReadiness] = useState<ProjectProvisioningReadinessUi | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [commandDrafts, setCommandDrafts] = useState<Record<string, string>>({});
  const [repositoryDraft, setRepositoryDraft] = useState(projectPath);
  const [completionNotice, setCompletionNotice] = useState<string | null>(null);
  const requestSequence = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  const taskRef = useRef<HTMLElement | null>(null);

  const evaluate = useCallback(
    async (preserveCurrent = false): Promise<boolean> => {
      const sequence = ++requestSequence.current;
      activeRequest.current?.abort();
      const controller = new AbortController();
      activeRequest.current = controller;
      if (!preserveCurrent) {
        setLoadState('loading');
      }
      setErrorMessage(null);

      try {
        const result = await session.client.getProvisioningReadiness(projectId, controller.signal);
        if (controller.signal.aborted || sequence !== requestSequence.current) {
          return false;
        }
        setReadiness(result);
        setLoadState('loaded');
        setCommandDrafts((previous) => {
          const next = { ...previous };
          for (const check of result.checks) {
            if (!(check.code in next) && check.detectedValue) {
              next[check.code] = check.detectedValue;
            }
          }
          return next;
        });
        return true;
      } catch (error: unknown) {
        if (controller.signal.aborted || sequence !== requestSequence.current) {
          return false;
        }
        setReadiness(null);
        setLoadState('unavailable');
        setErrorMessage(describeError(error, 'IronDev could not evaluate this repository.'));
        return false;
      }
    },
    [projectId, session.client]
  );

  useEffect(() => {
    setReadiness(null);
    setCommandDrafts({});
    setRepositoryDraft(projectPath);
    setCompletionNotice(null);
    void evaluate();
    return () => activeRequest.current?.abort();
  }, [evaluate, projectPath]);

  const model = useMemo(() => (readiness ? adaptProjectSetup(readiness) : null), [readiness]);
  const taskKey = model ? `${model.nextAction.kind}:${model.nextAction.checkCode ?? ''}` : loadState;

  useEffect(() => {
    if (loadState === 'loaded' && model && !model.isReady) {
      taskRef.current?.focus();
    }
  }, [loadState, model, taskKey]);

  const runMutation = async <T,>(
    action: () => Promise<T>,
    failureMessage: string,
    afterSuccess?: (result: T) => void
  ) => {
    if (busy) {
      return;
    }
    activeRequest.current?.abort();
    setBusy(true);
    setErrorMessage(null);
    try {
      const result = await action();
      afterSuccess?.(result);
      await evaluate(true);
    } catch (error: unknown) {
      setErrorMessage(describeError(error, failureMessage));
    } finally {
      setBusy(false);
    }
  };

  const confirmCommand = async () => {
    if (!model?.currentCheck) {
      return;
    }
    const check = model.currentCheck;
    const commandText = (commandDrafts[check.code] ?? check.detectedValue).trim();
    if (!commandText) {
      setErrorMessage(`${check.label} is required.`);
      return;
    }
    const commandType = check.code === 'BuildCommand' ? 'Build' : 'Test';
    await runMutation(
      () => session.client.saveProjectCommand(projectId, commandType, commandText),
      `The ${commandType.toLowerCase()} command was not saved.`
    );
  };

  const confirmProfile = async () => {
    if (!model?.source.proposedProfile) {
      setErrorMessage('No detected project structure is available to confirm.');
      return;
    }
    await runMutation(
      () => session.client.saveProjectProfile(projectId, model.source.proposedProfile as Record<string, unknown>),
      'The project structure was not saved.'
    );
  };

  const saveRepository = async () => {
    const localPath = repositoryDraft.trim();
    if (!localPath) {
      setErrorMessage('Repository path is required.');
      return;
    }
    await runMutation(async () => {
      await session.client.updateProjectLocalPath(projectId, localPath);
      setCommandDrafts({});
      await projects.refreshProjectContext();
    }, 'The repository path was not saved.');
  };

  const indexProject = async () => {
    await runMutation(
      () => session.client.indexProject(projectId),
      'The configured source tree was not indexed.',
      (outcome) => setCompletionNotice(
        outcome.indexResult
          ? `Indexed ${outcome.indexResult.storedFileCount} files`
          : outcome.message
      )
    );
  };

  const setBuilderWorkspacePermission = async (enabled: boolean) => {
    setCompletionNotice(null);
    await runMutation(
      () => session.client.setBuilderWorkspacePermission(projectId, enabled),
      enabled
        ? 'Governed Builder workspace writes were not enabled.'
        : 'Governed Builder workspace writes were not disabled.'
    );
  };

  const statusLabel =
    loadState === 'loading'
      ? 'Checking'
      : loadState === 'unavailable'
        ? 'Status unavailable'
        : model?.isReady
          ? 'Ready'
          : 'Setup required';
  const statusTone = loadState === 'unavailable' ? 'unavailable' : model?.isReady ? 'ready' : 'attention';

  return (
    <main
      className={`fl-root fl-project-setup${entryMode ? ' fl-project-setup--entry' : ''}`}
      data-testid="flow.projectSetup"
    >
      <ProjectSetupHeader
        projectName={projectName}
        repositoryPath={repositoryDraft || projectPath}
        statusLabel={statusLabel}
        statusTone={statusTone}
        onBackToProjects={onBackToProjects}
      />

      <div className="fl-project-setup__body">
        <div className="fl-project-setup__intro" aria-live="polite">
          {loadState === 'loading' ? (
            <p>Checking project setup...</p>
          ) : loadState === 'unavailable' ? (
            <p>Setup status is unavailable. Nothing has been marked ready.</p>
          ) : model?.isReady ? (
            <p>The repository, commands, project structure, and required run inputs have been confirmed.</p>
          ) : (
            <p>
              {model?.blockedCount ?? 0} item{model?.blockedCount === 1 ? '' : 's'} need attention before governed runs can start.
            </p>
          )}
        </div>

        {completionNotice ? (
          <section className="fl-project-setup__completion" data-testid="flow.projectSetup.completion" aria-live="polite">
            <strong>{completionNotice}</strong>
            <button className="fl-btn" type="button" onClick={() => setCompletionNotice(null)}>
              Continue setup
            </button>
          </section>
        ) : null}

        {loadState === 'loading' ? (
          <section className="fl-project-setup__next fl-project-setup__next--loading" aria-label="Checking project setup">
            <div className="fl-project-setup__skeleton" />
            <div className="fl-project-setup__skeleton fl-project-setup__skeleton--short" />
            <div className="fl-project-setup__skeleton" />
          </section>
        ) : loadState === 'unavailable' ? (
          <section className="fl-project-setup__unavailable" data-testid="flow.projectSetup.unavailable">
            <h2>Setup status is unavailable</h2>
            <p>IronDev could not evaluate this repository. Nothing has been marked ready.</p>
            {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
            <div className="fl-project-setup__task-actions">
              <button className="fl-btn fl-pri" type="button" disabled={busy} onClick={() => void evaluate()}>
                Retry setup check
              </button>
            </div>
          </section>
        ) : model?.isReady ? (
          <section className="fl-project-setup__ready" data-testid="flow.projectSetup.ready">
            <p className="fl-project-setup__ready-mark" aria-hidden="true">OK</p>
            <h2>Ready for governed runs</h2>
            <p>The backend has confirmed the project setup.</p>
            <button className="fl-btn fl-pri" type="button" data-testid="flow.projectSetup.openBoard" onClick={onOpenBoard}>
              Open Board
            </button>
            <button
              className="fl-btn"
              type="button"
              data-testid="flow.projectSetup.disableBuilderApply"
              disabled={busy}
              onClick={() => void setBuilderWorkspacePermission(false)}
            >
              {busy ? 'Disabling governed Builder writes...' : 'Disable governed Builder writes'}
            </button>
            {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
          </section>
        ) : model ? (
          <section
            ref={taskRef}
            className="fl-project-setup__next"
            data-testid="flow.projectSetup.next"
            aria-labelledby="project-setup-task-title"
            tabIndex={-1}
          >
            <ProjectSetupTask
              model={model}
              commandValue={model.currentCheck ? commandDrafts[model.currentCheck.code] ?? model.currentCheck.detectedValue : ''}
              repositoryValue={repositoryDraft}
              busy={busy}
              errorMessage={errorMessage}
              onCommandChange={(value) => {
                if (model.currentCheck) {
                  setCommandDrafts((previous) => ({ ...previous, [model.currentCheck!.code]: value }));
                }
              }}
              onConfirmCommand={() => void confirmCommand()}
              onConfirmProfile={() => void confirmProfile()}
              onRepositoryChange={(value) => setRepositoryDraft(value)}
              onSaveRepository={() => void saveRepository()}
              onRecheck={() => void evaluate(true)}
              onIndexProject={() => void indexProject()}
              onEnableBuilderApply={() => void setBuilderWorkspacePermission(true)}
            />
          </section>
        ) : null}

        {model && !model.isReady ? (
          <ProjectSetupChecklist checks={model.checklist} currentCheckCode={model.nextAction.checkCode} />
        ) : null}
        {model ? <ProjectSetupDetails checks={model.checks} /> : null}

        {!model?.isReady ? (
          <div className="fl-project-setup__actions">
            <button className="fl-btn" type="button" data-testid="flow.projectSetup.openBoardForShaping" onClick={onOpenBoard}>
              Open Board for shaping
            </button>
            <span>Governed runs remain blocked until setup is complete.</span>
          </div>
        ) : null}
      </div>
    </main>
  );
}
