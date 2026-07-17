import { useEffect, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface StartProjectScreenProps {
  onBack: () => void;
  onProjectStarted: (projectId: number) => void;
}

function describeStartError(error: unknown): string {
  if (error instanceof IronDevApiError && error.body && typeof error.body === 'object') {
    const body = error.body as { message?: unknown; error?: unknown };
    if (typeof body.message === 'string') return body.message;
    if (typeof body.error === 'string') return body.error;
  }
  return error instanceof Error ? error.message : 'The project could not be started.';
}

export function StartProjectScreen({ onBack, onProjectStarted }: StartProjectScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const errorRef = useRef<HTMLDivElement | null>(null);
  const operationIdRef = useRef<string>(crypto.randomUUID());
  const [name, setName] = useState('');
  const [isStarting, setIsStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  useEffect(() => {
    if (startError) errorRef.current?.focus();
  }, [startError]);

  const startProject = async () => {
    const trimmedName = name.trim();
    if (!trimmedName) {
      setStartError('Project name is required.');
      return;
    }

    setIsStarting(true);
    setStartError(null);
    try {
      const started = await session.client.startProject(trimmedName, operationIdRef.current);
      await project.selectProjectContext(started.projectId);
      onProjectStarted(started.projectId);
    } catch (error: unknown) {
      setStartError(describeStartError(error));
    } finally {
      setIsStarting(false);
    }
  };

  return (
    <section className="fl-connect-project" data-testid="flow.startProject" aria-labelledby="start-project-title">
      <div className="fl-auth-intro">
        <p className="fl-plabel">Project</p>
        <h1 id="start-project-title" ref={headingRef} className="fl-h1" tabIndex={-1}>
          Start a new project
        </h1>
        <p className="fl-sub">
          Create the project now, then shape the idea in Workbench.
          <br />
          Repository setup happens later.
        </p>
      </div>

      <div className="fl-connect-project__form">
        {startError ? (
          <div ref={errorRef} className="fl-error" data-testid="flow.startProject.error" role="alert" tabIndex={-1}>
            {startError}
          </div>
        ) : null}

        <label className="fl-auth-field">
          Project name
          <input
            data-testid="flow.startProject.name"
            aria-invalid={startError !== null && name.trim().length === 0}
            autoComplete="off"
            value={name}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !isStarting) void startProject();
            }}
          />
        </label>

        <div className="fl-connect-project__actions">
          <button className="fl-btn" data-testid="flow.startProject.back" type="button" disabled={isStarting} onClick={onBack}>
            Back to projects
          </button>
          <button
            className="fl-btn fl-pri"
            data-testid="flow.startProject.submit"
            type="button"
            disabled={isStarting}
            onClick={() => void startProject()}
          >
            {isStarting ? 'Starting...' : 'Start project'}
          </button>
        </div>
      </div>
    </section>
  );
}
