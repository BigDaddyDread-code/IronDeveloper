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
        <MetadataRow label="Mode" value={
          <StatusBadge status={
            latestResponse?.mode === 'Formalization'
              ? 'ready'
              : latestResponse?.mode === 'Confirmation'
                ? 'warning'
                : 'neutral'
          }>
            {latestResponse?.mode ?? 'Exploration'}
          </StatusBadge>
        } />
        <MetadataRow
          label="Trace"
          value={
            <StatusBadge status={latestResponse?.traceId ? 'ready' : 'neutral'}>
              {latestResponse?.traceId ? `#${latestResponse.traceId}` : 'No trace id yet'}
            </StatusBadge>
          }
        />
        {latestResponse?.dogfoodTraceId ? (
          <MetadataRow
            label="Dogfood trace"
            value={
              <StatusBadge status="ready">
                {latestResponse.dogfoodTraceId}{latestResponse.dogfoodTracePath ? ` (${latestResponse.dogfoodTracePath})` : ''}
              </StatusBadge>
            }
          />
        ) : null}
        <MetadataRow
          label="Context summary"
          value={
            <StatusBadge status={latestResponse?.contextSummary ? 'ready' : 'neutral'}>
              {latestResponse?.contextSummary ? 'Returned' : 'Pending'}
            </StatusBadge>
          }
        />
        <p>{latestResponse?.contextSummary ?? 'No context summary was returned yet.'}</p>
        {latestResponse?.reasoningSummary ? <p>{latestResponse.reasoningSummary}</p> : null}
        {latestResponse?.disambiguationQuestion ? (
          <p>
            <strong>Next choice:</strong> {latestResponse.disambiguationQuestion}
          </p>
        ) : null}
        {latestResponse?.reasoningTrace && latestResponse.reasoningTrace.length > 0 ? (
          <details open>
            <summary>Reasoning trace (from backend)</summary>
            <ul>
              {latestResponse.reasoningTrace.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </details>
        ) : null}
      </section>
      <ChatSourceList response={latestResponse} />
      <ChatSuggestedActions
        hasResponse={Boolean(latestResponse)}
        responseText={latestResponseText}
        governanceActions={latestResponse?.governanceActions ?? []}
        hasGovernanceActions={Boolean(latestResponse?.showGovernanceActions)}
        mode={latestResponse?.mode ?? null}
      />
    </aside>
  );
}
