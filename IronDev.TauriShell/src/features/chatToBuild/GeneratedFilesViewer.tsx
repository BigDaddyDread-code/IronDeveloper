import { useEffect, useMemo, useState } from 'react';
import type { GeneratedCodeFile } from '../../api/types';

export function GeneratedFilesViewer({ files }: { files: GeneratedCodeFile[] }) {
  const [selectedPath, setSelectedPath] = useState<string | null>(null);
  const selectedFile = useMemo(
    () => files.find((file) => file.relativePath === selectedPath) ?? files[0] ?? null,
    [files, selectedPath]
  );

  useEffect(() => {
    if (files.length === 0) {
      setSelectedPath(null);
      return;
    }

    if (!selectedPath || !files.some((file) => file.relativePath === selectedPath)) {
      setSelectedPath(files[0].relativePath);
    }
  }, [files, selectedPath]);

  return (
    <section className="chat-build-files" data-testid="chat-build.generatedFiles">
      <div className="section-heading">
        <p className="eyebrow">Generated files</p>
        <h2>Review-only file set</h2>
      </div>
      {files.length > 0 ? (
        <div className="chat-build-file-viewer">
          <div className="chat-build-file-tabs" role="list" aria-label="Generated files">
            {files.map((file) => (
              <button
                key={`${file.relativePath}-${file.sha256}`}
                type="button"
                className={`chat-build-file-tab${file.relativePath === selectedFile?.relativePath ? ' chat-build-file-tab--active' : ''}`}
                onClick={() => setSelectedPath(file.relativePath)}
              >
                <span>{file.relativePath}</span>
                <code>{file.sha256.slice(0, 12)}</code>
              </button>
            ))}
          </div>
          {selectedFile ? (
            <article className="chat-build-file">
              <header>
                <h3>{selectedFile.relativePath}</h3>
                <span>{selectedFile.sha256}</span>
              </header>
              <pre>{selectedFile.content}</pre>
            </article>
          ) : null}
        </div>
      ) : (
        <p className="state-muted">No generated files are present in the review package.</p>
      )}
    </section>
  );
}
