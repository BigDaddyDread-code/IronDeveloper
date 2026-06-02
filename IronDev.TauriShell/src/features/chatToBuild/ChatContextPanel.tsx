import type { ChatCompletionResponse } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { ChatSourceList } from './ChatSourceList';
import { ChatSuggestedActions } from './ChatSuggestedActions';

interface ChatContextPanelProps {
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
  isCollapsed: boolean;
  onToggleCollapsed: () => void;
}

export function ChatContextPanel({
  latestResponse,
  latestResponseText,
  projectLabel,
  isCollapsed,
  onToggleCollapsed
}: ChatContextPanelProps) {
  if (isCollapsed) {
    return null;
  }

  return (
    <aside className="chat-context-panel" data-testid="chat.contextPanel">
      <div className="chat-context-panel__header">
        <div className="section-heading">
          <p className="eyebrow">Context</p>
          <h3>Sources and actions</h3>
        </div>
        <CommandButton
          type="button"
          variant="subtle"
          testId="chat.contextPanel.toggle"
          onClick={onToggleCollapsed}
        >
          Hide
        </CommandButton>
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
