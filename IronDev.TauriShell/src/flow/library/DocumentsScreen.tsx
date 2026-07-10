import { useEffect, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ProjectDocument, ProjectDocumentVersion } from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';
import { RouteOutcomeScreen, type RouteOutcomeKind } from '../components/RouteOutcomeScreen';
import {
  documentPath,
  documentVersionPath,
  libraryPath,
  navigateProductPath,
  projectPath
} from '../navigation/productRoutes';

interface DocumentsScreenProps {
  projectId: number;
  documentId: number | null;
  versionId: number | null;
  action: 'upload' | null;
}

type DocumentLoadState = 'loading' | 'ready' | 'notFound' | 'permission' | 'unavailable';
type StatusFilter = 'all' | 'active' | 'archived';

export function DocumentsScreen({ projectId, documentId, versionId, action }: DocumentsScreenProps) {
  if (action === 'upload') {
    return (
      <RouteOutcomeScreen
        kind="notImplemented"
        title="Document upload is not implemented"
        message="The current backend stores versioned Markdown documents but exposes no file upload or processing contract."
        nextSafeAction="Return to Documents. No file has been accepted or processed."
        actionLabel="Back to Documents"
        onAction={() => navigateProductPath(libraryPath(projectId, 'documents'))}
      />
    );
  }

  return documentId === null ? (
    <DocumentList projectId={projectId} />
  ) : (
    <DocumentDetail projectId={projectId} documentId={documentId} requestedVersionId={versionId} />
  );
}

function DocumentList({ projectId }: { projectId: number }) {
  const session = useSessionContext();
  const [documents, setDocuments] = useState<ProjectDocument[]>([]);
  const [currentVersions, setCurrentVersions] = useState<Map<number, ProjectDocumentVersion | null>>(new Map());
  const [loadState, setLoadState] = useState<DocumentLoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [typeFilter, setTypeFilter] = useState('all');

  useEffect(() => {
    const controller = new AbortController();

    const load = async () => {
      setLoadState('loading');
      setErrorMessage('');
      try {
        const loaded = await session.client.getProjectDocuments(projectId, '*', controller.signal);
        const versionEntries = await Promise.all(
          loaded.map(async (document): Promise<[number, ProjectDocumentVersion | null] | null> => {
            if (!document.id) return null;
            if (!document.currentVersionId) return [document.id, null];
            try {
              const version = await session.client.getProjectDocumentCurrentVersion(
                projectId,
                document.id,
                controller.signal
              );
              return [document.id, version];
            } catch {
              return [document.id, null];
            }
          })
        );
        if (controller.signal.aborted) return;
        setDocuments(loaded);
        setCurrentVersions(new Map(versionEntries.filter((entry): entry is [number, ProjectDocumentVersion | null] => entry !== null)));
        setLoadState('ready');
      } catch (error) {
        if (controller.signal.aborted) return;
        const outcome = classifyLoadError(error);
        setLoadState(outcome.kind);
        setErrorMessage(outcome.message);
      }
    };

    void load();
    return () => controller.abort();
  }, [projectId, reloadKey, session.client]);

  const types = useMemo(
    () => [...new Set(documents.map((document) => document.documentType?.trim()).filter(isNonEmpty))].sort(),
    [documents]
  );
  const visibleDocuments = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return [...documents]
      .filter((document) => {
        const status = document.status?.toLowerCase() ?? '';
        const matchesStatus = statusFilter === 'all' || status === statusFilter;
        const matchesType = typeFilter === 'all' || document.documentType === typeFilter;
        const matchesQuery = normalizedQuery.length === 0 || document.title?.toLowerCase().includes(normalizedQuery);
        return matchesStatus && matchesType && matchesQuery;
      })
      .sort((left, right) => timestamp(right.updatedAtUtc ?? right.createdAtUtc) - timestamp(left.updatedAtUtc ?? left.createdAtUtc));
  }, [documents, query, statusFilter, typeFilter]);

  if (loadState !== 'loading' && loadState !== 'ready') {
    const copy = outcomeCopy(loadState, 'documents');
    return (
      <RouteOutcomeScreen
        kind={copy.kind}
        title={copy.title}
        message={errorMessage || copy.message}
        nextSafeAction={copy.nextSafeAction}
        actionLabel="Retry"
        onAction={() => setReloadKey((value) => value + 1)}
      />
    );
  }

  return (
    <section className="fl-documents" data-testid="flow.documents.list" aria-labelledby="documents-heading">
      <header className="fl-documents-toolbar">
        <div>
          <h2 id="documents-heading">Project documents</h2>
          <p>Versioned project context. A document does not grant approval or source-mutation authority.</p>
        </div>
        <button className="fl-btn" type="button" onClick={() => navigateProductPath(projectPath(projectId, 'chat'))}>
          Open Chat
        </button>
      </header>

      {loadState === 'loading' ? (
        <p className="fl-documents-loading" data-testid="flow.documents.loading">Loading project documents...</p>
      ) : documents.length === 0 ? (
        <div className="fl-documents-empty" data-testid="flow.documents.empty">
          <h3>No project documents</h3>
          <p>Save an eligible Chat response as a discussion document to create durable project context.</p>
          <button className="fl-btn fl-pri" type="button" onClick={() => navigateProductPath(projectPath(projectId, 'chat'))}>
            Open Chat
          </button>
        </div>
      ) : (
        <>
          <div className="fl-documents-filters">
            <label>
              <span>Search</span>
              <input
                type="search"
                value={query}
                placeholder="Search by title"
                onChange={(event) => setQuery(event.target.value)}
                data-testid="flow.documents.search"
              />
            </label>
            <div className="fl-documents-segmented" role="group" aria-label="Document status">
              {(['all', 'active', 'archived'] as const).map((status) => (
                <button
                  key={status}
                  type="button"
                  aria-pressed={statusFilter === status}
                  onClick={() => setStatusFilter(status)}
                >
                  {capitalize(status)}
                </button>
              ))}
            </div>
            <label>
              <span>Type</span>
              <select value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}>
                <option value="all">All types</option>
                {types.map((type) => <option key={type} value={type}>{formatDocumentType(type)}</option>)}
              </select>
            </label>
          </div>

          {visibleDocuments.length === 0 ? (
            <div className="fl-documents-filter-empty" data-testid="flow.documents.filteredEmpty">
              <p>No documents match these filters.</p>
              <button
                className="fl-btn"
                type="button"
                onClick={() => {
                  setQuery('');
                  setStatusFilter('all');
                  setTypeFilter('all');
                }}
              >
                Reset filters
              </button>
            </div>
          ) : (
            <div className="fl-documents-table" aria-label="Project documents">
              <div className="fl-documents-row fl-documents-row--head" aria-hidden="true">
                <span>Document</span>
                <span>Type</span>
                <span>Version</span>
                <span>State</span>
                <span>Updated</span>
              </div>
              <ul>
                {visibleDocuments.map((document) => {
                  const currentVersion = document.id ? currentVersions.get(document.id) : null;
                  return (
                    <li key={document.id ?? document.slug ?? document.title}>
                      <button
                        className="fl-documents-row fl-documents-row--item"
                        type="button"
                        disabled={!document.id}
                        onClick={() => document.id && navigateProductPath(documentPath(projectId, document.id))}
                        data-testid={`flow.documents.open.${document.id ?? 'unknown'}`}
                      >
                        <span className="fl-documents-title">
                          <strong>{document.title?.trim() || 'Untitled document'}</strong>
                          <small>{document.updatedBy || document.createdBy || 'Author unavailable'}</small>
                        </span>
                        <span data-label="Type">{formatDocumentType(document.documentType)}</span>
                        <span data-label="Version">{currentVersion?.versionLabel || 'Unavailable'}</span>
                        <span data-label="State"><DocumentStatus status={document.status} /></span>
                        <span data-label="Updated">{formatDate(document.updatedAtUtc ?? document.createdAtUtc)}</span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function DocumentDetail({
  projectId,
  documentId,
  requestedVersionId
}: {
  projectId: number;
  documentId: number;
  requestedVersionId: number | null;
}) {
  const session = useSessionContext();
  const [document, setDocument] = useState<ProjectDocument | null>(null);
  const [versions, setVersions] = useState<ProjectDocumentVersion[]>([]);
  const [loadState, setLoadState] = useState<DocumentLoadState>('loading');
  const [errorMessage, setErrorMessage] = useState('');
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoadState('loading');
      setErrorMessage('');
      try {
        const [loadedDocument, loadedVersions] = await Promise.all([
          session.client.getProjectDocument(projectId, documentId, controller.signal),
          session.client.getProjectDocumentVersions(projectId, documentId, controller.signal)
        ]);
        if (controller.signal.aborted) return;
        setDocument(loadedDocument);
        setVersions(sortVersions(loadedVersions));
        setLoadState('ready');
      } catch (error) {
        if (controller.signal.aborted) return;
        const outcome = classifyLoadError(error);
        setLoadState(outcome.kind);
        setErrorMessage(outcome.message);
      }
    };

    void load();
    return () => controller.abort();
  }, [documentId, projectId, reloadKey, session.client]);

  const selectedVersion = requestedVersionId === null
    ? versions.find((version) => version.id === document?.currentVersionId) ?? versions[0] ?? null
    : versions.find((version) => version.id === requestedVersionId) ?? null;

  if (loadState === 'loading') {
    return <p className="fl-documents-loading" data-testid="flow.documents.detailLoading">Loading document...</p>;
  }

  if (loadState !== 'ready') {
    const copy = outcomeCopy(loadState, 'document');
    return (
      <RouteOutcomeScreen
        kind={copy.kind}
        title={copy.title}
        message={errorMessage || copy.message}
        nextSafeAction={copy.nextSafeAction}
        actionLabel={loadState === 'notFound' ? 'Back to Documents' : 'Retry'}
        onAction={() => loadState === 'notFound'
          ? navigateProductPath(libraryPath(projectId, 'documents'))
          : setReloadKey((value) => value + 1)}
      />
    );
  }

  if (requestedVersionId !== null && selectedVersion === null) {
    return (
      <RouteOutcomeScreen
        kind="notFound"
        title="Document version not found"
        message="The requested immutable version is not part of this project document."
        nextSafeAction="Open the document's current version and choose from its backend-owned history."
        actionLabel="Open current version"
        onAction={() => navigateProductPath(documentPath(projectId, documentId))}
      />
    );
  }

  if (!document || !selectedVersion) {
    return (
      <RouteOutcomeScreen
        kind="blocked"
        title="Document has no readable version"
        message="The document identity exists, but the backend returned no immutable version content."
        nextSafeAction="Return to Documents. No content has been inferred from the document identity."
        actionLabel="Back to Documents"
        onAction={() => navigateProductPath(libraryPath(projectId, 'documents'))}
      />
    );
  }

  return (
    <section className="fl-document-detail" data-testid="flow.documents.detail" aria-labelledby="document-title">
      <div className="fl-document-breadcrumbs" aria-label="Document path">
        <button type="button" onClick={() => navigateProductPath(libraryPath(projectId, 'documents'))}>Documents</button>
        <span>/</span>
        <button type="button" onClick={() => navigateProductPath(documentPath(projectId, documentId))}>{document.title}</button>
        {requestedVersionId !== null ? <><span>/</span><strong>{selectedVersion.versionLabel}</strong></> : null}
      </div>

      <header className="fl-document-heading">
        <div>
          <p className="fl-plabel">{formatDocumentType(document.documentType)}</p>
          <h2 id="document-title">{document.title?.trim() || 'Untitled document'}</h2>
          <p>{selectedVersion.changeSummary?.trim() || 'No change summary was recorded for this version.'}</p>
        </div>
        <div className="fl-document-statuses">
          <DocumentStatus status={document.status} />
          <DocumentStatus status={selectedVersion.status} />
        </div>
      </header>

      <div className="fl-document-layout">
        <aside className="fl-document-versions" aria-labelledby="document-versions-heading">
          <div>
            <h3 id="document-versions-heading">Version history</h3>
            <span>{versions.length}</span>
          </div>
          {versions.map((version) => {
            const isCurrent = version.id === document.currentVersionId;
            const isSelected = version.id === selectedVersion.id;
            return (
              <button
                key={version.id ?? version.versionLabel}
                type="button"
                className={isSelected ? 'fl-document-version fl-document-version--selected' : 'fl-document-version'}
                disabled={!version.id}
                onClick={() => version.id && navigateProductPath(documentVersionPath(projectId, documentId, version.id))}
                data-testid={`flow.documents.version.${version.id ?? 'unknown'}`}
              >
                <span><strong>{version.versionLabel || 'Unlabelled version'}</strong>{isCurrent ? <small>Current</small> : null}</span>
                <span>{formatDate(version.createdAtUtc)}</span>
              </button>
            );
          })}
        </aside>

        <article className="fl-document-content">
          <div className="fl-document-content-heading">
            <div>
              <p className="fl-plabel">Immutable version</p>
              <h3>{selectedVersion.versionLabel || 'Unlabelled version'}</h3>
            </div>
            <dl>
              <div><dt>Created by</dt><dd>{selectedVersion.createdBy || 'Unavailable'}</dd></div>
              <div><dt>Created</dt><dd>{formatDate(selectedVersion.createdAtUtc)}</dd></div>
            </dl>
          </div>
          <pre data-testid="flow.documents.content">{selectedVersion.contentMarkdown || 'This version contains no Markdown content.'}</pre>
          <p className="fl-document-boundary">This version is immutable project context. It is not approval, readiness evidence, or permission to mutate source.</p>
        </article>
      </div>
    </section>
  );
}

function DocumentStatus({ status }: { status: string | null | undefined }) {
  const label = status?.trim() || 'Unknown';
  const normalized = label.toLowerCase();
  const tone = normalized === 'active' || normalized === 'approved'
    ? 'ready'
    : normalized === 'archived' || normalized === 'superseded'
      ? 'warning'
      : 'neutral';
  return <StatusBadge status={tone}>{label}</StatusBadge>;
}

function classifyLoadError(error: unknown): { kind: Exclude<DocumentLoadState, 'loading' | 'ready'>; message: string } {
  if (error instanceof IronDevApiError) {
    const message = readApiErrorMessage(error);
    if (error.status === 404) return { kind: 'notFound', message };
    if (error.status === 401 || error.status === 403) return { kind: 'permission', message };
    return { kind: 'unavailable', message };
  }
  return { kind: 'unavailable', message: error instanceof Error ? error.message : 'The Documents API did not respond.' };
}

function readApiErrorMessage(error: IronDevApiError) {
  if (error.body && typeof error.body === 'object') {
    const body = error.body as Record<string, unknown>;
    for (const key of ['error', 'message', 'detail', 'title']) {
      const value = body[key];
      if (typeof value === 'string' && value.trim()) return value.trim();
    }
  }
  return error.message;
}

function outcomeCopy(state: Exclude<DocumentLoadState, 'loading' | 'ready'>, subject: 'documents' | 'document') {
  const plural = subject === 'documents';
  const copy: Record<Exclude<DocumentLoadState, 'loading' | 'ready'>, {
    kind: RouteOutcomeKind;
    title: string;
    message: string;
    nextSafeAction: string;
  }> = {
    notFound: {
      kind: 'notFound',
      title: plural ? 'Documents route not found' : 'Document not found',
      message: plural ? 'The project Documents route is unavailable.' : 'The requested document is not available in this project.',
      nextSafeAction: 'Return to the project Documents list. No substitute content has been shown.'
    },
    permission: {
      kind: 'permission',
      title: plural ? 'Documents access denied' : 'Document access denied',
      message: 'The backend refused access.',
      nextSafeAction: 'Retry after access is restored or return to the project. No restricted metadata has been inferred.'
    },
    unavailable: {
      kind: 'unavailable',
      title: plural ? 'Documents unavailable' : 'Document unavailable',
      message: 'The backend did not return document state.',
      nextSafeAction: 'Retry the backend read. Existing document state has not been replaced.'
    }
  };
  return copy[state];
}

function sortVersions(versions: ProjectDocumentVersion[]) {
  return [...versions].sort((left, right) => {
    const majorDifference = (right.versionMajor ?? 0) - (left.versionMajor ?? 0);
    return majorDifference || (right.versionMinor ?? 0) - (left.versionMinor ?? 0);
  });
}

function formatDocumentType(value: string | null | undefined) {
  const type = value?.trim();
  return type ? type.replace(/([a-z])([A-Z])/g, '$1 $2') : 'Type unavailable';
}

function formatDate(value: string | null | undefined) {
  if (!value) return 'Unavailable';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Unavailable' : date.toLocaleString();
}

function timestamp(value: string | null | undefined) {
  if (!value) return 0;
  const time = new Date(value).getTime();
  return Number.isNaN(time) ? 0 : time;
}

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function isNonEmpty(value: string | undefined): value is string {
  return Boolean(value);
}
