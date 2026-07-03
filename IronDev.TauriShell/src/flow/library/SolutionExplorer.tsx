import { useEffect, useMemo, useState } from 'react';
import type { ProjectFileSummary } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

// The tree renders what IronDev actually knows — the code index in SQL — not the live
// filesystem. Staleness is therefore a visible property of the tree itself: if the index
// is stale, the tree may be lying, and the banner says exactly that.

const pageSize = 500;
const maxFiles = 4000;

interface TreeDirectory {
  name: string;
  path: string;
  directories: Map<string, TreeDirectory>;
  files: ProjectFileSummary[];
}

function buildTree(files: ProjectFileSummary[]): TreeDirectory {
  const root: TreeDirectory = { name: '', path: '', directories: new Map(), files: [] };
  for (const file of files) {
    const segments = file.filePath.replace(/\\/g, '/').split('/');
    let node = root;
    for (let i = 0; i < segments.length - 1; i += 1) {
      const segment = segments[i];
      let child = node.directories.get(segment);
      if (!child) {
        child = {
          name: segment,
          path: node.path.length > 0 ? `${node.path}/${segment}` : segment,
          directories: new Map(),
          files: []
        };
        node.directories.set(segment, child);
      }
      node = child;
    }
    node.files.push(file);
  }
  return root;
}

function fileName(path: string): string {
  const normalized = path.replace(/\\/g, '/');
  const index = normalized.lastIndexOf('/');
  return index >= 0 ? normalized.slice(index + 1) : normalized;
}

interface DirectoryNodeProps {
  directory: TreeDirectory;
  depth: number;
  collapsed: Set<string>;
  onToggle: (path: string) => void;
  selectedId: number | null;
  onSelect: (file: ProjectFileSummary) => void;
}

function DirectoryNode({ directory, depth, collapsed, onToggle, selectedId, onSelect }: DirectoryNodeProps) {
  const isCollapsed = collapsed.has(directory.path);
  const indent = { paddingLeft: depth * 16 };

  return (
    <>
      {directory.path.length > 0 ? (
        <button className="fl-tree-row fl-tree-dir" style={indent} onClick={() => onToggle(directory.path)}>
          {isCollapsed ? '▸' : '▾'} {directory.name}
        </button>
      ) : null}
      {!isCollapsed || directory.path.length === 0 ? (
        <>
          {[...directory.directories.values()]
            .sort((a, b) => a.name.localeCompare(b.name))
            .map((child) => (
              <DirectoryNode
                key={child.path}
                directory={child}
                depth={directory.path.length === 0 ? depth : depth + 1}
                collapsed={collapsed}
                onToggle={onToggle}
                selectedId={selectedId}
                onSelect={onSelect}
              />
            ))}
          {directory.files.map((file) => (
            <button
              key={file.id}
              className={file.id === selectedId ? 'fl-tree-row fl-tree-file fl-tree-sel' : 'fl-tree-row fl-tree-file'}
              style={{ paddingLeft: (directory.path.length === 0 ? depth : depth + 1) * 16 }}
              onClick={() => onSelect(file)}
            >
              {fileName(file.filePath)}
            </button>
          ))}
        </>
      ) : null}
    </>
  );
}

export function SolutionExplorer() {
  const session = useSessionContext();
  const project = useProjectContext();

  const [files, setFiles] = useState<ProjectFileSummary[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [truncated, setTruncated] = useState(false);
  const [filter, setFilter] = useState('');
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [selected, setSelected] = useState<ProjectFileSummary | null>(null);

  const selectedProject = project.projects.find((candidate) => candidate.id === project.selectedProjectId);
  const indexedFileCount = selectedProject?.indexedFileCount ?? null;
  const lastIndexedUtc = selectedProject?.lastIndexedUtc ?? null;
  const indexingStatus = selectedProject?.indexingStatus ?? null;
  const looksStale =
    lastIndexedUtc === null ||
    (indexingStatus !== null && indexingStatus.toLowerCase().includes('stale')) ||
    (indexingStatus !== null && indexingStatus.toLowerCase().includes('fail'));

  useEffect(() => {
    if (project.selectedProjectId === null) {
      setFiles([]);
      setLoadState('ready');
      return;
    }
    const controller = new AbortController();
    const projectId = project.selectedProjectId;

    const loadAll = async () => {
      setLoadState('loading');
      const collected: ProjectFileSummary[] = [];
      try {
        for (let skip = 0; skip < maxFiles; skip += pageSize) {
          const page = await session.client.listCodeIndexFiles(projectId, skip, pageSize, controller.signal);
          collected.push(...page);
          if (page.length < pageSize) {
            setTruncated(false);
            break;
          }
          if (collected.length >= maxFiles) {
            setTruncated(true);
            break;
          }
        }
        setFiles(collected);
        setLoadState('ready');
      } catch (error: unknown) {
        if (controller.signal.aborted) {
          return;
        }
        setErrorMessage(error instanceof Error ? error.message : 'Could not load the indexed file list.');
        setLoadState('error');
      }
    };

    void loadAll();
    return () => controller.abort();
  }, [session.client, project.selectedProjectId]);

  const visibleFiles = useMemo(() => {
    const query = filter.trim().toLowerCase();
    if (query.length === 0) {
      return files;
    }
    return files.filter((file) => file.filePath.toLowerCase().includes(query));
  }, [files, filter]);

  const tree = useMemo(() => buildTree(visibleFiles), [visibleFiles]);

  const toggleDirectory = (path: string) => {
    setCollapsed((previous) => {
      const next = new Set(previous);
      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }
      return next;
    });
  };

  return (
    <div>
      {looksStale ? (
        <div className="fl-qbox" style={{ marginTop: 0, marginBottom: 12 }} data-testid="flow.explorer.stale">
          <span>
            {lastIndexedUtc === null
              ? 'This project has not been indexed — the tree below may be empty or lying. Index the project before trusting it.'
              : `Index status is ${indexingStatus ?? 'unknown'} — this tree may be out of date.`}
          </span>
        </div>
      ) : null}

      {errorMessage ? <div className="fl-error">{errorMessage}</div> : null}

      <div className="fl-cols" style={{ gridTemplateColumns: '0.9fr 1.1fr', marginTop: 0 }}>
        <div className="fl-panel-box">
          <p className="fl-plabel">
            Indexed tree{indexedFileCount !== null ? ` · ${indexedFileCount} files` : ''}
            {truncated ? ' · showing first 4000' : ''}
          </p>
          <input
            className="fl-select"
            style={{ width: '100%', marginBottom: 10 }}
            placeholder="Filter by path"
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            data-testid="flow.explorer.filter"
          />
          {loadState === 'loading' ? (
            <p className="fl-empty">Loading indexed files…</p>
          ) : visibleFiles.length === 0 ? (
            <p className="fl-empty">
              {files.length === 0 ? 'No indexed files. Index the project first.' : 'Nothing matches the filter.'}
            </p>
          ) : (
            <div className="fl-tree" data-testid="flow.explorer.tree">
              <DirectoryNode
                directory={tree}
                depth={0}
                collapsed={collapsed}
                onToggle={toggleDirectory}
                selectedId={selected?.id ?? null}
                onSelect={setSelected}
              />
            </div>
          )}
        </div>

        <div className="fl-panel-box">
          {selected === null ? (
            <>
              <p className="fl-plabel">File detail</p>
              <p className="fl-empty">Select a file to see what the index knows about it.</p>
            </>
          ) : (
            <>
              <p className="fl-plabel">{fileName(selected.filePath)}</p>
              <div className="fl-kv">
                <strong>Path</strong>
                <span style={{ fontFamily: 'var(--fl-mono)', fontSize: 12 }}>{selected.filePath}</span>
                <strong>Type</strong>
                <span>{selected.fileExtension || '—'}</span>
                <strong>Last indexed</strong>
                <span>{new Date(selected.lastIndexedDate).toLocaleString()}</span>
              </div>
              <p className="fl-plabel" style={{ marginTop: 16 }}>
                Related work
              </p>
              <p className="fl-empty" style={{ textAlign: 'left', padding: '4px 0' }}>
                Tickets, proposals, and receipts touching this file appear here once the operation lookup
                read model lands. The index never grants authority — this panel only reports.
              </p>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
