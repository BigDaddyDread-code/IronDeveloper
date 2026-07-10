import { useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ProjectToolCatalogueResponse, ProjectToolDetailResponse, ProjectToolSummary } from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';
import { RouteOutcomeScreen } from '../components/RouteOutcomeScreen';
import { libraryPath, navigateProductPath, toolPath } from '../navigation/productRoutes';

interface ToolsScreenProps {
  projectId: number;
  toolId: string | null;
}

type ToolLoadState = 'loading' | 'ready' | 'empty' | 'notFound' | 'unavailable';

export function ToolsScreen({ projectId, toolId }: ToolsScreenProps) {
  return toolId ? <ToolDetail projectId={projectId} toolId={toolId} /> : <ToolCatalogue projectId={projectId} />;
}

function ToolCatalogue({ projectId }: { projectId: number }) {
  const session = useSessionContext();
  const [catalogue, setCatalogue] = useState<ProjectToolCatalogueResponse | null>(null);
  const [loadState, setLoadState] = useState<ToolLoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoadState('loading');
      setErrorMessage('');
      try {
        const loaded = await session.client.getProjectTools(projectId, controller.signal);
        setCatalogue(loaded);
        setLoadState(loaded.tools.length === 0 ? 'empty' : 'ready');
      } catch (error) {
        if (controller.signal.aborted) return;
        setLoadState(error instanceof IronDevApiError && error.status === 404 ? 'notFound' : 'unavailable');
        setErrorMessage(describeError(error, 'The governed tool catalogue could not be loaded.'));
      }
    };
    void load();
    return () => controller.abort();
  }, [projectId, reloadKey, session.client]);

  if (loadState === 'loading') {
    return <p className="fl-empty" data-testid="flow.tools.loading">Loading governed tools...</p>;
  }

  if (loadState === 'notFound') {
    return (
      <RouteOutcomeScreen
        kind="notFound"
        title="Project tool catalogue not found"
        message="The backend did not return this project in the current tenant."
        nextSafeAction="Return to Library and select a project returned by the backend."
        actionLabel="Back to Library"
        onAction={() => navigateProductPath(libraryPath(projectId, 'explorer'))}
      />
    );
  }

  if (loadState === 'unavailable') {
    return (
      <RouteOutcomeScreen
        kind="unavailable"
        title="Project tools are unavailable"
        message={errorMessage}
        nextSafeAction="Retry the backend-owned catalogue. No tool state has been changed."
        actionLabel="Retry"
        onAction={() => setReloadKey((current) => current + 1)}
      />
    );
  }

  return (
    <section className="fl-tools" data-testid="flow.tools.catalogue" aria-labelledby="tools-heading">
      <header className="fl-tools__heading">
        <div>
          <p className="fl-plabel">Governed tools</p>
          <h2 id="tools-heading">Project tool catalogue</h2>
          <p>Registered capabilities and their current project-use boundary.</p>
        </div>
        <span>{catalogue?.tools.length ?? 0} registered</span>
      </header>

      {loadState === 'empty' ? (
        <div className="fl-tools__empty" data-testid="flow.tools.empty">
          <h3>No governed tools are registered</h3>
          <p>The backend returned an empty registry. No Add or Request action is available in this slice.</p>
        </div>
      ) : (
        <div className="fl-tool-list" aria-label="Registered governed tools">
          {catalogue?.tools.map((tool) => (
            <ToolRow key={tool.toolId} projectId={projectId} tool={tool} />
          ))}
        </div>
      )}

      {catalogue?.boundary ? <p className="fl-tool-boundary">{catalogue.boundary}</p> : null}
    </section>
  );
}

function ToolRow({ projectId, tool }: { projectId: number; tool: ProjectToolSummary }) {
  return (
    <button
      className="fl-tool-row"
      type="button"
      onClick={() => navigateProductPath(toolPath(projectId, tool.toolId))}
      data-testid={`flow.tools.open.${tool.toolId}`}
    >
      <span className="fl-tool-row__identity">
        <span>
          <strong>{tool.displayName}</strong>
          <small>{tool.category}</small>
        </span>
        <StatusBadge status="ready">{tool.registrationStatus}</StatusBadge>
      </span>
      <span className="fl-tool-row__description">{tool.description}</span>
      <span className="fl-tool-row__states">
        <span><small>Connection</small><strong>{tool.connectionStatus}</strong></span>
        <span><small>Project use</small><strong>{tool.projectUseStatus}</strong></span>
        <span><small>Direct invocation</small><strong>{tool.directInvocationStatus}</strong></span>
        <span><small>Health</small><strong>{tool.healthStatus}</strong></span>
      </span>
      <span className="fl-tool-row__scope">{tool.effectiveScopeSummary}</span>
    </button>
  );
}

function ToolDetail({ projectId, toolId }: { projectId: number; toolId: string }) {
  const session = useSessionContext();
  const [tool, setTool] = useState<ProjectToolDetailResponse | null>(null);
  const [loadState, setLoadState] = useState<ToolLoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoadState('loading');
      setErrorMessage('');
      try {
        setTool(await session.client.getProjectTool(projectId, toolId, controller.signal));
        setLoadState('ready');
      } catch (error) {
        if (controller.signal.aborted) return;
        setLoadState(error instanceof IronDevApiError && error.status === 404 ? 'notFound' : 'unavailable');
        setErrorMessage(describeError(error, 'The governed tool definition could not be loaded.'));
      }
    };
    void load();
    return () => controller.abort();
  }, [projectId, reloadKey, session.client, toolId]);

  if (loadState === 'loading') {
    return <p className="fl-empty" data-testid="flow.tools.detail.loading">Loading tool definition...</p>;
  }

  if (loadState === 'notFound') {
    return (
      <RouteOutcomeScreen
        kind="notFound"
        title="Governed tool not found"
        message="The requested tool is not registered for this backend catalogue."
        nextSafeAction="Return to Tools and choose a definition returned by the backend."
        actionLabel="Back to Tools"
        onAction={() => navigateProductPath(libraryPath(projectId, 'tools'))}
      />
    );
  }

  if (loadState === 'unavailable' || !tool) {
    return (
      <RouteOutcomeScreen
        kind="unavailable"
        title="Governed tool is unavailable"
        message={errorMessage || 'The backend did not return a tool definition.'}
        nextSafeAction="Retry this read-only definition. No tool state has been changed."
        actionLabel="Retry"
        onAction={() => setReloadKey((current) => current + 1)}
      />
    );
  }

  const capabilities = [
    ['State mutation', tool.capabilities.mutatesState],
    ['Nested tool calls', tool.capabilities.allowsNestedCalls],
    ['File writes', tool.capabilities.allowsFileWrites],
    ['Process execution', tool.capabilities.allowsProcessExecution],
    ['Network access', tool.capabilities.allowsNetworkAccess],
    ['Workspace mutation', tool.capabilities.allowsWorkspaceMutation]
  ] as const;

  return (
    <section className="fl-tool-detail" data-testid="flow.tools.detail" aria-labelledby="tool-detail-heading">
      <div className="fl-document-breadcrumbs" aria-label="Tool path">
        <button type="button" onClick={() => navigateProductPath(libraryPath(projectId, 'tools'))}>Tools</button>
        <span>/</span>
        <strong>{tool.displayName}</strong>
      </div>

      <header className="fl-tool-detail__heading">
        <div>
          <p className="fl-plabel">{tool.category}</p>
          <h2 id="tool-detail-heading">{tool.displayName}</h2>
          <p>{tool.description}</p>
        </div>
        <StatusBadge status="ready">{tool.registrationStatus}</StatusBadge>
      </header>

      <div className="fl-tool-detail__states">
        <ToolState label="Tenant connection" value={tool.connectionStatus} />
        <ToolState label="Project use" value={tool.projectUseStatus} />
        <ToolState label="Direct invocation" value={tool.directInvocationStatus} />
        <ToolState label="Health" value={tool.healthStatus} />
      </div>

      <section className="fl-tool-detail__section">
        <h3>Effective scope</h3>
        <p>{tool.effectiveScopeSummary}</p>
        <div className="fl-tool-capabilities" aria-label="Declared tool capabilities">
          {capabilities.map(([label, allowed]) => (
            <span key={label} data-allowed={allowed ? 'true' : 'false'}>
              <strong>{label}</strong>
              <small>{allowed ? 'Declared' : 'Not declared'}</small>
            </span>
          ))}
        </div>
      </section>

      <section className="fl-tool-detail__section">
        <h3>Governed use</h3>
        <ToolList title="Allowed callers" items={tool.allowedCallers} />
        <ToolList title="Evidence produced" items={tool.evidenceKinds} />
      </section>

      <details className="fl-tool-detail__details">
        <summary>Definition details</summary>
        <dl>
          <div><dt>Tool ID</dt><dd><code>{tool.toolId}</code></dd></div>
          <div><dt>Definition version</dt><dd>{tool.definitionVersion}</dd></div>
          <div><dt>Input contract</dt><dd><code>{tool.inputContract}</code></dd></div>
          <div><dt>Output contract</dt><dd><code>{tool.outputContract}</code></dd></div>
        </dl>
      </details>

      <p className="fl-tool-boundary">{tool.boundary}</p>
    </section>
  );
}

function ToolState({ label, value }: { label: string; value: string }) {
  return <div><small>{label}</small><strong>{value}</strong></div>;
}

function ToolList({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="fl-tool-detail__list">
      <strong>{title}</strong>
      {items.length > 0 ? <ul>{items.map((item) => <li key={item}>{item}</li>)}</ul> : <p>None declared.</p>}
    </div>
  );
}

function describeError(error: unknown, fallback: string) {
  if (error instanceof IronDevApiError) {
    const body = error.body as { error?: string; message?: string } | null | undefined;
    return body?.error ?? body?.message ?? `${fallback} HTTP ${error.status}.`;
  }
  return error instanceof Error ? error.message : fallback;
}
