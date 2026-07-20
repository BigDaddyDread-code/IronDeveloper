import { useEffect, useState } from 'react';
import type { BaWorkingDraft, ChatCompletionResponse, ChatDocumentSource, CreateProjectChannelRequest, ProjectChannelChatSummary, ProjectChatSession, ProjectTicket } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { ChatComposer } from './ChatComposer';
import { ChatContextPanel } from './ChatContextPanel';
import { ChatSessionRail } from './ChatSessionRail';
import { ChatTicketDraftReview } from './ChatTicketDraftReview';
import { ChatThread } from './ChatThread';
import { ProjectUnderstandingPanel } from './ProjectUnderstandingPanel';
import type { ChatAgentRunState, ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';
import type { ProjectUnderstandingController } from './useProjectUnderstanding';
import type { WorkbenchCommandNotice, WorkbenchCommandToken } from './workbenchCommands';

interface ChatWorkspaceProps {
  sessions: ProjectChatSession[];
  channels: ProjectChannelChatSummary[];
  canCreateChannels: boolean;
  channelLoadState: 'loading' | 'ready' | 'error';
  channelError: string | null;
  activeSessionId: number | null;
  messages: ChatWorkspaceMessage[];
  composerValue: string;
  isSending: boolean;
  isCancellingAgentRun: boolean;
  agentRun: ChatAgentRunState | null;
  conversationAuthorityEnabled: boolean;
  projectUnderstanding: ProjectUnderstandingController;
  hasUnresolvedDurableOperation: boolean;
  agentCancellationDeliveryUnresolved: boolean;
  boundAgentRunChatSessionId: number | null;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  documentSources: ChatDocumentSource[];
  documentSourceLoadState: 'idle' | 'loading' | 'ready' | 'error';
  documentSourceError: string | null;
  selectedDocumentSource: ChatDocumentSource | null;
  commandNotice: WorkbenchCommandNotice | null;
  errorMessage: string | null;
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
  onOpenSession: (sessionId: number) => void;
  onOpenChannel: (slug: string) => void;
  onCreateChannel: (request: CreateProjectChannelRequest) => Promise<ProjectChannelChatSummary>;
  onRetryChannels: () => void;
  onStartNewConversation: () => void;
  onComposerChange: (value: string) => void;
  onSelectCommand: (token: WorkbenchCommandToken) => void;
  onSend: (request?: ChatSendRequest) => void;
  onCancelAgentRun: () => void;
  onLoadDocumentSources: () => void;
  onSelectDocumentSource: (source: ChatDocumentSource | null) => void;
  onReviewProjectState: () => void;
  onSaveDiscussion: (messageId: string) => void;
  onKeepDiscussingBaDraft: () => void;
  onAskNextBaQuestion: (draft: BaWorkingDraft) => void;
  onEditBaDraft: (draft: BaWorkingDraft) => void;
  onCreateTicketFromDraft: (draft: BaWorkingDraft) => Promise<ProjectTicket>;
  onOpenWorkItem: (ticket: ProjectTicket) => void;
}

export function ChatWorkspace({
  sessions,
  channels,
  canCreateChannels,
  channelLoadState,
  channelError,
  activeSessionId,
  messages,
  composerValue,
  isSending,
  isCancellingAgentRun,
  agentRun,
  conversationAuthorityEnabled,
  projectUnderstanding,
  hasUnresolvedDurableOperation,
  agentCancellationDeliveryUnresolved,
  boundAgentRunChatSessionId,
  disabledReason,
  sendDisabledReason,
  documentSources,
  documentSourceLoadState,
  documentSourceError,
  selectedDocumentSource,
  commandNotice,
  errorMessage,
  latestResponse,
  latestResponseText,
  projectLabel,
  onOpenSession,
  onOpenChannel,
  onCreateChannel,
  onRetryChannels,
  onStartNewConversation,
  onComposerChange,
  onSelectCommand,
  onSend,
  onCancelAgentRun,
  onLoadDocumentSources,
  onSelectDocumentSource,
  onReviewProjectState,
  onSaveDiscussion,
  onKeepDiscussingBaDraft,
  onAskNextBaQuestion,
  onEditBaDraft,
  onCreateTicketFromDraft,
  onOpenWorkItem
}: ChatWorkspaceProps) {
  const [isContextOpen, setIsContextOpen] = useState(false);
  const [isSessionRailOpen, setIsSessionRailOpen] = useState(false);
  const [reviewedDraft, setReviewedDraft] = useState<BaWorkingDraft | null>(null);

  useEffect(() => {
    setIsContextOpen(false);
    setIsSessionRailOpen(false);
    setReviewedDraft(null);
  }, [activeSessionId]);

  useEffect(() => {
    if (latestResponse?.baDraft) {
      setIsContextOpen(true);
    }
  }, [latestResponse?.baDraft]);

  const startDraft = (prompt: string) => {
    onComposerChange(prompt);
    window.setTimeout(() => document.getElementById('chat-composer-input')?.focus(), 0);
  };

  return (
    <section className="chat-workspace-panel" data-testid="chat.workspace">
      <ChatSessionRail
        sessions={sessions}
        channels={channels}
        activeSessionId={activeSessionId}
        activeChannelSlug={null}
        canCreateChannels={canCreateChannels}
        channelLoadState={channelLoadState}
        channelError={channelError}
        directNavigationDisabledReason={conversationAuthorityEnabled && hasUnresolvedDurableOperation
          ? 'Delivery is unresolved. Replay the exact operation before changing conversations.'
          : conversationAuthorityEnabled && isSending
            ? 'A governed Business Analyst turn is being submitted or processed.'
          : conversationAuthorityEnabled && boundAgentRunChatSessionId !== null
            ? 'The active Workbench session is bound to this governed conversation.'
            : null}
        isOpen={isSessionRailOpen}
        onClose={() => setIsSessionRailOpen(false)}
        onOpenSession={onOpenSession}
        onOpenChannel={onOpenChannel}
        onCreateChannel={onCreateChannel}
        onRetryChannels={onRetryChannels}
        onStartNewConversation={() => {
          setIsSessionRailOpen(false);
          onStartNewConversation();
        }}
      />
      <div className="chat-conversation">
        <header className="chat-page-header">
          <div>
            <h1>Workshop</h1>
            <p>
              {projectLabel} <span aria-hidden="true">/</span> Direct with Business Analyst
            </p>
          </div>
          <div className="chat-page-header__actions">
            <CommandButton
              type="button"
              variant="subtle"
              className="chat-session-rail__toggle"
              testId="chat.sessions.toggle"
              onClick={() => setIsSessionRailOpen(true)}
            >
              Conversations
            </CommandButton>
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.contextPanel.show"
              onClick={() => setIsContextOpen((current) => !current)}
            >
              {conversationAuthorityEnabled
                ? isContextOpen ? 'Close project context' : 'Project context'
                : isContextOpen ? 'Close details' : 'Conversation details'}
            </CommandButton>
          </div>
        </header>
        <div className={`chat-workspace-layout ${isContextOpen ? '' : 'chat-workspace-layout--context-collapsed'}`.trim()}>
          <div className="chat-workspace-layout__thread">
            <ChatThread
              messages={messages}
              isSending={isSending}
              agentRun={agentRun}
              onSaveDiscussion={onSaveDiscussion}
              onViewSources={() => setIsContextOpen(true)}
              onReviewProjectState={onReviewProjectState}
              onStartDraft={startDraft}
            />
            {errorMessage ? <p className="state-error" data-testid="chat.error">{errorMessage}</p> : null}
            <ChatComposer
              value={composerValue}
              isSending={isSending}
              isCancellingAgentRun={isCancellingAgentRun}
              agentRun={agentRun}
              conversationAuthorityEnabled={conversationAuthorityEnabled}
              agentCancellationDeliveryUnresolved={agentCancellationDeliveryUnresolved}
              disabledReason={disabledReason}
              sendDisabledReason={sendDisabledReason}
              documentSources={documentSources}
              documentSourceLoadState={documentSourceLoadState}
              documentSourceError={documentSourceError}
              selectedDocumentSource={selectedDocumentSource}
              commandNotice={commandNotice}
              onChange={onComposerChange}
              onSelectCommand={onSelectCommand}
              onSend={() => onSend()}
              onCancelAgentRun={onCancelAgentRun}
              onLoadDocumentSources={onLoadDocumentSources}
              onSelectDocumentSource={onSelectDocumentSource}
            />
          </div>
          {conversationAuthorityEnabled ? (
            isContextOpen ? (
              <ProjectUnderstandingPanel
                controller={projectUnderstanding}
                onClose={() => setIsContextOpen(false)}
              />
            ) : null
          ) : (
            <ChatContextPanel
              latestResponse={latestResponse}
              latestResponseText={latestResponseText}
              projectLabel={projectLabel}
              isCollapsed={!isContextOpen}
              onToggleCollapsed={() => setIsContextOpen((current) => !current)}
              onKeepDiscussingBaDraft={onKeepDiscussingBaDraft}
              onAskNextBaQuestion={onAskNextBaQuestion}
              onEditBaDraft={onEditBaDraft}
              onReviewBaDraft={setReviewedDraft}
            />
          )}
        </div>
      </div>
      {reviewedDraft ? (
        <ChatTicketDraftReview
          draft={reviewedDraft}
          projectLabel={projectLabel}
          sourceSessionId={activeSessionId}
          onClose={() => setReviewedDraft(null)}
          onCreateTicket={onCreateTicketFromDraft}
          onOpenWorkItem={onOpenWorkItem}
        />
      ) : null}
    </section>
  );
}
