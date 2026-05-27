import type { GeneratedCodeFile } from '../../api/types';

export function GeneratedFilesViewer({ files }: { files: GeneratedCodeFile[] }) {
  return (
    <section className="chat-build-files" data-testid="chat-build.generatedFiles">
      <div className="section-heading">
        <p className="eyebrow">Generated files</p>
        <h2>Files</h2>
      </div>
      {files.length > 0 ? (
        <div className="chat-build-file-list">
          {files.map((file) => (
            <article key={`${file.relativePath}-${file.sha256}`} className="chat-build-file">
              <header>
                <h3>{file.relativePath}</h3>
                <span>{file.sha256.slice(0, 12)}</span>
              </header>
              <pre>{file.content}</pre>
            </article>
          ))}
        </div>
      ) : (
        <p className="state-muted">No generated files are present in the review package.</p>
      )}
    </section>
  );
}
