import type { ChatCompletionResponse } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { getChatModeGate } from './chatGovernanceGate';
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
  const gate = getChatModeGate(latestResponse);
  const auditSourceLabel = formatAuditSource(latestResponse?.auditSource);

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
          <StatusBadge status={gate.modeBadgeStatus}>
            {gate.mode ?? 'Unknown'}
          </StatusBadge>
        } />
        <MetadataRow
          label="Trace"
          value={
            <StatusBadge status={latestResponse?.routeTraceId || latestResponse?.dogfoodTraceId || latestResponse?.traceId ? 'ready' : 'neutral'}>
              {latestResponse?.routeTraceId ??
                latestResponse?.dogfoodTraceId ??
                (latestResponse?.traceId ? `#${latestResponse.traceId}` : 'No trace id yet')}
            </StatusBadge>
          }
        />
        {auditSourceLabel ? (
          <MetadataRow
            label="Audit source"
            value={
              <StatusBadge status={latestResponse?.auditSource === 'durable' ? 'ready' : latestResponse?.auditSource === 'tags' ? 'warning' : 'neutral'}>
                {auditSourceLabel}
              </StatusBadge>
            }
          />
        ) : null}
        {latestResponse?.auditHasFallbackEvidence ? (
          <MetadataRow
            label="Fallback evidence"
            value={<StatusBadge status="warning">Present</StatusBadge>}
          />
        ) : null}
        {latestResponse?.clarification ? (
          <MetadataRow
            label="Clarification"
            value={
              <StatusBadge status={latestResponse.clarification.required ? 'warning' : 'neutral'}>
                {latestResponse.clarification.required
                  ? `${latestResponse.clarification.kind} required`
                  : 'None'}
              </StatusBadge>
            }
          />
        ) : null}
        {typeof gate.confidence === 'number' ? (
          <MetadataRow
            label="Mode confidence"
            value={
              <StatusBadge status={gate.confidence >= 0.75 ? 'ready' : 'warning'}>
                {`${(gate.confidence * 100).toFixed(0)}%`}
              </StatusBadge>
            }
          />
        ) : null}
        {gate.reason ? (
          <p className="chat-context-panel__mode-reason">
            <strong>Mode reason:</strong> {gate.reason}
          </p>
        ) : null}
        {latestResponse?.auditFallbackReason ? <p>{latestResponse.auditFallbackReason}</p> : null}
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
        gate={gate}
      />
    </aside>
  );
}

function formatAuditSource(source: ChatCompletionResponse['auditSource'] | undefined) {
  if (source === 'durable') {
    return 'Durable audit';
  }

  if (source === 'tags') {
    return 'Tags replay fallback';
  }

  if (source === 'live') {
    return 'Live response';
  }

  if (source === 'none') {
    return 'No audit metadata';
  }

  return null;
}
