import { useEffect, useState } from 'react';
import type { BaWorkingDraft, ChatCompletionResponse, ChatDocumentSource, ProjectChatSession, ProjectTicket } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { ChatComposer } from './ChatComposer';
import { ChatContextPanel } from './ChatContextPanel';
import { ChatSessionRail } from './ChatSessionRail';
import { ChatTicketDraftReview } from './ChatTicketDraftReview';
import { ChatThread } from './ChatThread';
import type { ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';

interface ChatWorkspaceProps {
  sessions: ProjectChatSession[];
  activeSessionId: number | null;
  messages: ChatWorkspaceMessage[];
  composerValue: string;
  isSending: boolean;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  documentSources: ChatDocumentSource[];
  documentSourceLoadState: 'idle' | 'loading' | 'ready' | 'error';
  documentSourceError: string | null;
  selectedDocumentSource: ChatDocumentSource | null;
  errorMessage: string | null;
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
  onOpenSession: (sessionId: number) => void;
  onStartNewConversation: () => void;
  onComposerChange: (value: string) => void;
  onSend: (request?: ChatSendRequest) => void;
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
  activeSessionId,
  messages,
  composerValue,
  isSending,
  disabledReason,
  sendDisabledReason,
  documentSources,
  documentSourceLoadState,
  documentSourceError,
  selectedDocumentSource,
  errorMessage,
  latestResponse,
  latestResponseText,
  projectLabel,
  onOpenSession,
  onStartNewConversation,
  onComposerChange,
  onSend,
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
        activeSessionId={activeSessionId}
        isOpen={isSessionRailOpen}
        onClose={() => setIsSessionRailOpen(false)}
        onOpenSession={onOpenSession}
        onStartNewConversation={() => {
          setIsSessionRailOpen(false);
          onStartNewConversation();
        }}
      />
      <div className="chat-conversation">
        <header className="chat-page-header">
          <div>
            <h1>Chat</h1>
            <p>
              {projectLabel} <span aria-hidden="true">/</span> Direct with IronDev
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
              {isContextOpen ? 'Close details' : 'Conversation details'}
            </CommandButton>
          </div>
        </header>
        <div className={`chat-workspace-layout ${isContextOpen ? '' : 'chat-workspace-layout--context-collapsed'}`.trim()}>
          <div className="chat-workspace-layout__thread">
            <ChatThread
              messages={messages}
              isSending={isSending}
              onSaveDiscussion={onSaveDiscussion}
              onViewSources={() => setIsContextOpen(true)}
              onReviewProjectState={onReviewProjectState}
              onStartDraft={startDraft}
            />
            {errorMessage ? <p className="state-error" data-testid="chat.error">{errorMessage}</p> : null}
            <ChatComposer
              value={composerValue}
              isSending={isSending}
              disabledReason={disabledReason}
              sendDisabledReason={sendDisabledReason}
              documentSources={documentSources}
              documentSourceLoadState={documentSourceLoadState}
              documentSourceError={documentSourceError}
              selectedDocumentSource={selectedDocumentSource}
              onChange={onComposerChange}
              onSend={() => onSend()}
              onLoadDocumentSources={onLoadDocumentSources}
              onSelectDocumentSource={onSelectDocumentSource}
            />
          </div>
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
