import type { BaWorkingDraft, ChatCompletionResponse } from '../../api/types';
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
  onKeepDiscussingBaDraft: () => void;
  onAskNextBaQuestion: (draft: BaWorkingDraft) => void;
  onEditBaDraft: (draft: BaWorkingDraft) => void;
  onConfirmBaDraft: (draft: BaWorkingDraft) => void;
}

export function ChatContextPanel({
  latestResponse,
  latestResponseText,
  projectLabel,
  isCollapsed,
  onToggleCollapsed,
  onKeepDiscussingBaDraft,
  onAskNextBaQuestion,
  onEditBaDraft,
  onConfirmBaDraft
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
        <MetadataRow
          label="Route source"
          value={
            <StatusBadge status={latestResponse?.routeSource ? 'ready' : 'neutral'}>
              {latestResponse?.routeSource ?? 'Unknown'}
            </StatusBadge>
          }
        />
        {latestResponse?.routeChallenge ? (
          <MetadataRow
            label="Route challenge"
            value={
              <StatusBadge status="warning">
                {latestResponse.routeChallenge.suggestedMode ?? 'Suggested route'}
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
      {latestResponse?.baDraft ? (
        <BaDraftPanel
          draft={latestResponse.baDraft}
          onKeepDiscussing={onKeepDiscussingBaDraft}
          onAskNextQuestion={onAskNextBaQuestion}
          onEditDraft={onEditBaDraft}
          onConfirmDraft={onConfirmBaDraft}
        />
      ) : null}
      <ChatSourceList response={latestResponse} />
      <ChatSuggestedActions
        hasResponse={Boolean(latestResponse)}
        responseText={latestResponseText}
        gate={gate}
      />
    </aside>
  );
}

interface BaDraftPanelProps {
  draft: BaWorkingDraft;
  onKeepDiscussing: () => void;
  onAskNextQuestion: (draft: BaWorkingDraft) => void;
  onEditDraft: (draft: BaWorkingDraft) => void;
  onConfirmDraft: (draft: BaWorkingDraft) => void;
}

function BaDraftPanel({
  draft,
  onKeepDiscussing,
  onAskNextQuestion,
  onEditDraft,
  onConfirmDraft
}: BaDraftPanelProps) {
  const rules = cleanList(draft.businessRules);
  const criteria = cleanList(draft.acceptanceCriteria);
  const assumptions = cleanList(draft.assumptions);
  const openQuestions = cleanList(draft.openQuestions);
  const conflicts = cleanList(draft.potentialConflicts);
  const sourceIds = cleanList(draft.sourceMessageIds);
  const canConfirm = draft.readyForConfirmation === true && conflicts.length === 0;

  return (
    <section className="workflow-section chat-ba-draft" data-testid="chat.baDraft.panel">
      <div className="workflow-section__header">
        <div>
          <p className="eyebrow">BA draft</p>
          <h3>Draft work item</h3>
        </div>
        <StatusBadge status={canConfirm ? 'ready' : conflicts.length > 0 ? 'warning' : 'neutral'}>
          {draft.suggestedArtifact ?? 'Draft'}
        </StatusBadge>
      </div>
      <MetadataRow label="Title" value={draft.candidateTitle ?? 'Untitled'} />
      {draft.problem ? <p data-testid="chat.baDraft.problem">{draft.problem}</p> : null}
      {draft.proposedChange ? <p data-testid="chat.baDraft.proposedChange">{draft.proposedChange}</p> : null}
      <DraftList title="Rules" items={rules} testId="chat.baDraft.rules" />
      <DraftList title="Acceptance criteria" items={criteria} ordered testId="chat.baDraft.criteria" />
      <DraftList title="Assumptions" items={assumptions} testId="chat.baDraft.assumptions" />
      <DraftList title={openQuestions.length === 1 ? 'Open question' : 'Open questions'} items={openQuestions} testId="chat.baDraft.questions" />
      <DraftList title="Potential conflict" items={conflicts} testId="chat.baDraft.conflicts" />
      {sourceIds.length > 0 ? (
        <p className="chat-ba-draft__sources" data-testid="chat.baDraft.sources">
          <strong>Source messages:</strong> {sourceIds.join(', ')}
        </p>
      ) : null}
      {typeof draft.confidence === 'number' ? (
        <p className="chat-ba-draft__confidence" data-testid="chat.baDraft.confidence">
          <strong>Confidence:</strong> {Math.round(draft.confidence * 100)}%
        </p>
      ) : null}
      <div className="chat-ba-draft__actions" data-testid="chat.baDraft.actions">
        <CommandButton type="button" variant="subtle" testId="chat.baDraft.keepDiscussing" onClick={onKeepDiscussing}>
          Keep discussing
        </CommandButton>
        <CommandButton
          type="button"
          variant="secondary"
          testId="chat.baDraft.askNext"
          disabled={openQuestions.length === 0}
          onClick={() => onAskNextQuestion(draft)}
        >
          Ask next question
        </CommandButton>
        <CommandButton type="button" variant="subtle" testId="chat.baDraft.edit" onClick={() => onEditDraft(draft)}>
          Edit draft
        </CommandButton>
        <CommandButton
          type="button"
          variant="primary"
          testId="chat.baDraft.confirm"
          disabled={!canConfirm}
          onClick={() => onConfirmDraft(draft)}
        >
          Confirm as ticket
        </CommandButton>
      </div>
      {draft.boundary ? <p className="chat-ba-draft__boundary">{draft.boundary}</p> : null}
    </section>
  );
}

function DraftList({
  title,
  items,
  ordered = false,
  testId
}: {
  title: string;
  items: string[];
  ordered?: boolean;
  testId: string;
}) {
  if (items.length === 0) {
    return null;
  }

  const ListTag = ordered ? 'ol' : 'ul';
  return (
    <div className="chat-ba-draft__list" data-testid={testId}>
      <h4>{title}</h4>
      <ListTag>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ListTag>
    </div>
  );
}

function cleanList(values: string[] | null | undefined) {
  return values?.map((item) => item.trim()).filter(Boolean) ?? [];
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
