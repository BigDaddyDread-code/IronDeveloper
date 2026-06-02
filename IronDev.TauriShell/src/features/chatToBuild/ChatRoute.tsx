import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { ChatWorkspace } from './ChatWorkspace';
import { useProjectChat } from './useProjectChat';

interface ChatRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function ChatRoute({ route, onRouteReady }: ChatRouteProps) {
  const chat = useProjectChat();

  const routeSummary = useMemo(
    () => [
      { label: chat.projectLabel, testId: 'chat.summary.project' },
      { label: chat.messages.length > 0 ? `${chat.messages.length} message(s)` : 'Conversation ready', testId: 'chat.summary.messages' }
    ],
    [chat.messages.length, chat.projectLabel]
  );

  useEffect(() => {
    onRouteReady?.({
      workspaceCommands: [],
      workspaceBlockReason: chat.disabledReason,
      workspaceSummaryChips: routeSummary,
      blockReasonTestId: chat.disabledReason ? 'chat.blockedReason' : undefined
    });
  }, [chat.disabledReason, onRouteReady, routeSummary]);

  return (
    <main className="chat-route-workspace" data-testid="chat.route" aria-label={route.label}>
      <ChatWorkspace
        messages={chat.messages}
        composerValue={chat.draft}
        isSending={chat.isSending}
        disabledReason={chat.disabledReason}
        sendDisabledReason={chat.sendDisabledReason}
        buildBridgeDisabledReason={chat.buildBridgeDisabledReason}
        errorMessage={chat.errorMessage}
        latestResponse={chat.latestResponse}
        latestResponseText={chat.latestResponseText}
        projectLabel={chat.projectLabel}
        onComposerChange={chat.setDraft}
        onSend={chat.sendMessage}
        onReviewProjectState={chat.reviewProjectState}
        onContinueInBuild={chat.continueInBuild}
      />
    </main>
  );
}
