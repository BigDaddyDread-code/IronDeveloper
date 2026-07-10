import { useEffect, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface ConnectProjectScreenProps {
  onBack: () => void;
  onProjectCreated: (projectId: number) => void;
}

function suggestNameFromPath(path: string): string {
  return (
    path
      .trim()
      .replace(/[\\/]+$/, '')
      .split(/[\\/]+/)
      .filter(Boolean)
      .pop() ?? ''
  );
}

function isTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

function describeCreateError(error: unknown): string {
  if (error instanceof IronDevApiError) {
    const body = error.body;
    if (body && typeof body === 'object' && 'error' in body && typeof body.error === 'string') {
      return body.error;
    }
  }

  return error instanceof Error ? error.message : 'The project could not be connected.';
}

export function ConnectProjectScreen({ onBack, onProjectCreated }: ConnectProjectScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const errorRef = useRef<HTMLDivElement | null>(null);
  const [name, setName] = useState('');
  const [localPath, setLocalPath] = useState('');
  const [nameEdited, setNameEdited] = useState(false);
  const [isCreating, setIsCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const canBrowse = isTauriRuntime();

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  useEffect(() => {
    if (createError) {
      errorRef.current?.focus();
    }
  }, [createError]);

  const updatePath = (value: string) => {
    setLocalPath(value);
    if (!nameEdited && name.trim().length === 0) {
      setName(suggestNameFromPath(value));
    }
  };

  const browseForRepository = async () => {
    setCreateError(null);
    try {
      const dialog = await import('@tauri-apps/plugin-dialog');
      const selected = await dialog.open({ directory: true, multiple: false });
      if (typeof selected === 'string') {
        updatePath(selected);
      }
    } catch {
      setCreateError('Folder picker is unavailable here. Enter the full local repository path.');
    }
  };

  const createProject = async () => {
    const trimmedName = name.trim();
    const trimmedPath = localPath.trim();

    if (trimmedName.length === 0 || trimmedPath.length === 0) {
      setCreateError('Project name and local repository path are required.');
      return;
    }

    setIsCreating(true);
    setCreateError(null);

    try {
      const created = await session.client.createProject(trimmedName, trimmedPath);
      if (created.id === undefined || created.id === null) {
        setCreateError('The backend did not return a project id.');
        return;
      }

      await project.selectProjectContext(created.id);
      onProjectCreated(created.id);
    } catch (error: unknown) {
      setCreateError(describeCreateError(error));
    } finally {
      setIsCreating(false);
    }
  };

  return (
    <section className="fl-connect-project" data-testid="flow.connectProject" aria-labelledby="connect-project-title">
      <div className="fl-auth-intro">
        <p className="fl-plabel">Project</p>
        <h1 id="connect-project-title" ref={headingRef} className="fl-h1" tabIndex={-1}>
          Connect a project
        </h1>
        <p className="fl-sub">Add a local repository to this tenant.</p>
      </div>

      <div className="fl-connect-project__form">
        {createError ? (
          <div
            ref={errorRef}
            className="fl-error"
            data-testid="flow.chooser.create.error"
            role="alert"
            tabIndex={-1}
          >
            {createError}
          </div>
        ) : null}

        <label className="fl-auth-field">
          Project name
          <input
            data-testid="flow.chooser.create.name"
            aria-invalid={createError !== null && name.trim().length === 0}
            value={name}
            onChange={(event) => {
              setNameEdited(true);
              setName(event.target.value);
            }}
          />
        </label>

        <label className="fl-auth-field">
          Local repository path
          <span className="fl-connect-project__path-row">
            <input
              data-testid="flow.chooser.create.path"
              aria-invalid={createError !== null && localPath.trim().length === 0}
              value={localPath}
              onChange={(event) => updatePath(event.target.value)}
              placeholder="C:\\path\\to\\repo"
            />
            {canBrowse ? (
              <button
                className="fl-btn"
                data-testid="flow.connectProject.browse"
                type="button"
                onClick={() => void browseForRepository()}
              >
                Browse...
              </button>
            ) : null}
          </span>
        </label>
        <p className="fl-sub">Enter the full local repository path.</p>

        <div className="fl-connect-project__actions">
          <button className="fl-btn" data-testid="flow.connectProject.back" type="button" disabled={isCreating} onClick={onBack}>
            Back to projects
          </button>
          <button
            className="fl-btn fl-pri"
            data-testid="flow.chooser.create.submit"
            type="button"
            disabled={isCreating}
            onClick={() => void createProject()}
          >
            {isCreating ? 'Connecting...' : 'Connect project'}
          </button>
        </div>
      </div>
    </section>
  );
}
