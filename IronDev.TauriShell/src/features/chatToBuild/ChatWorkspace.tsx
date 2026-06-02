import { useState } from 'react';
import type { ChatCompletionResponse } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { Surface } from '../../design-system/Surface';
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
  buildBridgeDisabledReason: string | null;
  errorMessage: string | null;
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
  onComposerChange: (value: string) => void;
  onSend: (request?: ChatSendRequest) => void;
  onReviewProjectState: () => void;
  onContinueInBuild: () => void;
}

export function ChatWorkspace({
  messages,
  composerValue,
  isSending,
  disabledReason,
  sendDisabledReason,
  buildBridgeDisabledReason,
  errorMessage,
  latestResponse,
  latestResponseText,
  projectLabel,
  onComposerChange,
  onSend,
  onReviewProjectState,
  onContinueInBuild
}: ChatWorkspaceProps) {
  const [isContextOpen, setIsContextOpen] = useState(true);

  return (
    <Surface className="chat-workspace-panel" testId="chat.workspace">
      <div className={`chat-workspace-layout ${isContextOpen ? '' : 'chat-workspace-layout--context-collapsed'}`.trim()}>
        <div className="chat-workspace-layout__thread">
          <ChatThread messages={messages} isSending={isSending} />
          {errorMessage ? <p className="state-error" data-testid="chat.error">{errorMessage}</p> : null}
          <ChatComposer
            value={composerValue}
            isSending={isSending}
            disabledReason={disabledReason}
            sendDisabledReason={sendDisabledReason}
            onChange={onComposerChange}
            onSend={() => onSend()}
            onReviewProjectState={onReviewProjectState}
            onContinueInBuild={onContinueInBuild}
            buildBridgeDisabledReason={buildBridgeDisabledReason}
          />
        </div>
        <ChatContextPanel
          latestResponse={latestResponse}
          latestResponseText={latestResponseText}
          projectLabel={projectLabel}
          isCollapsed={!isContextOpen}
          onToggleCollapsed={() => setIsContextOpen((current) => !current)}
        />
        {!isContextOpen ? (
          <CommandButton
            type="button"
            variant="secondary"
            testId="chat.contextPanel.show"
            onClick={() => setIsContextOpen(true)}
          >
            Show Context
          </CommandButton>
        ) : null}
      </div>
    </Surface>
  );
}
