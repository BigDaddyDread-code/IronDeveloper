import type { ChatCompletionResponse } from '../../api/types';
import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { ChatSourceList } from './ChatSourceList';
import { ChatSuggestedActions } from './ChatSuggestedActions';

interface ChatContextPanelProps {
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
}

export function ChatContextPanel({ latestResponse, latestResponseText, projectLabel }: ChatContextPanelProps) {
  return (
    <aside className="chat-context-panel" data-testid="chat.contextPanel">
      <div className="section-heading">
        <p className="eyebrow">Context Used</p>
        <h3>Project signal</h3>
      </div>
      <section className="workflow-section">
        <MetadataRow label="Project" value={projectLabel} />
        <MetadataRow label="Trace" value={latestResponse?.traceId ? `#${latestResponse.traceId}` : 'No trace yet'} />
        <MetadataRow
          label="Context"
          value={
            <StatusBadge status={latestResponse?.contextSummary ? 'ready' : 'neutral'}>
              {latestResponse?.contextSummary ? 'Returned' : 'Pending'}
            </StatusBadge>
          }
        />
        <p>{latestResponse?.contextSummary ?? 'Context summary will appear after the backend returns a grounded response.'}</p>
      </section>
      <ChatSourceList response={latestResponse} />
      <ChatSuggestedActions hasResponse={Boolean(latestResponse)} responseText={latestResponseText} />
    </aside>
  );
}
