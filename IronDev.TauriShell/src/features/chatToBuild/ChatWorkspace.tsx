import type { ChatCompletionResponse } from '../../api/types';
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
  errorMessage: string | null;
  latestResponse: ChatCompletionResponse | null;
  latestResponseText: string | null;
  projectLabel: string;
  onComposerChange: (value: string) => void;
  onSend: (request?: ChatSendRequest) => void;
  onReviewProjectState: () => void;
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
  onReviewProjectState
}: ChatWorkspaceProps) {
  return (
    <Surface className="chat-workspace-panel" testId="chat.workspace">
      <div className="chat-workspace-layout">
        <aside className="chat-sessions" data-testid="chat.sessions">
          <div className="section-heading">
            <p className="eyebrow">Conversations</p>
            <h3>Current thread</h3>
          </div>
          <p className="state-muted">Project-scoped chat for the selected workspace.</p>
        </aside>
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
          />
        </div>
        <ChatContextPanel latestResponse={latestResponse} latestResponseText={latestResponseText} projectLabel={projectLabel} />
      </div>
    </Surface>
  );
}
