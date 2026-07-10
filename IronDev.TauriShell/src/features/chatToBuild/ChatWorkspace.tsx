import { useEffect, useState } from 'react';
import type { BaWorkingDraft, ChatCompletionResponse } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { ChatComposer } from './ChatComposer';
import { ChatContextPanel } from './ChatContextPanel';
import { ChatThread } from './ChatThread';
import type { ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';

interface ChatWorkspaceProps {
  messages: ChatWorkspaceMessage[];
  composerValue: string;
  isSending: boolean;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  errorMessage: string | null;
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
  onComposerChange: (value: string) => void;
  onSend: (request?: ChatSendRequest) => void;
  onReviewProjectState: () => void;
  onSaveDiscussion: (messageId: string) => void;
  onKeepDiscussingBaDraft: () => void;
  onAskNextBaQuestion: (draft: BaWorkingDraft) => void;
  onEditBaDraft: (draft: BaWorkingDraft) => void;
  onConfirmBaDraft: (draft: BaWorkingDraft) => void;
}

export function ChatWorkspace({
  messages,
  composerValue,
  isSending,
  disabledReason,
  sendDisabledReason,
  errorMessage,
  latestResponse,
  latestResponseText,
  projectLabel,
  onComposerChange,
  onSend,
  onReviewProjectState,
  onSaveDiscussion,
  onKeepDiscussingBaDraft,
  onAskNextBaQuestion,
  onEditBaDraft,
  onConfirmBaDraft
}: ChatWorkspaceProps) {
  const [isContextOpen, setIsContextOpen] = useState(false);

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
      <header className="chat-page-header">
        <div>
          <h1>Chat</h1>
          <p>
            {projectLabel} <span aria-hidden="true">/</span> Direct with IronDev
          </p>
        </div>
        <CommandButton
          type="button"
          variant="subtle"
          testId="chat.contextPanel.show"
          onClick={() => setIsContextOpen((current) => !current)}
        >
          {isContextOpen ? 'Close details' : 'Conversation details'}
        </CommandButton>
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
            onChange={onComposerChange}
            onSend={() => onSend()}
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
          onConfirmBaDraft={onConfirmBaDraft}
        />
      </div>
    </section>
  );
}
