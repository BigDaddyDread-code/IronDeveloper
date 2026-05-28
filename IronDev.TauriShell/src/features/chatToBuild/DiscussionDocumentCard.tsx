import { MetadataRow } from '../../components/MetadataRow';
import { Surface } from '../../design-system/Surface';
import type { SaveDiscussionResponse } from '../../api/types';

export function DiscussionDocumentCard({ document }: { document: SaveDiscussionResponse | null }) {
  return (
    <Surface className="chat-build-panel" testId="chat-build.discussionDocument">
      <div className="section-heading">
        <p className="eyebrow">Document</p>
        <h2>Saved discussion</h2>
      </div>
      {document ? (
        <div className="metadata-stack">
          <MetadataRow label="Document" value={document.documentId} />
          <MetadataRow label="Version" value={document.documentVersionId} />
        </div>
      ) : (
        <p className="state-muted">Save the discussion to create a project document.</p>
      )}
    </Surface>
  );
}
