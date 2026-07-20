import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import type { ProjectTicket } from '../../api/types';
import { RouteOutcomeScreen } from '../../flow/components/RouteOutcomeScreen';
import { ChatWorkspace } from './ChatWorkspace';
import { useProjectChat } from './useProjectChat';
import { useProjectChannels } from './useProjectChannels';
import { useProjectUnderstanding } from './useProjectUnderstanding';

interface ChatRouteProps {
  route: WorkspaceRoute;
  requestedSessionId: number | null;
  onOpenSession: (sessionId: number) => void;
  onOpenChannel: (slug: string) => void;
  onOpenLanding: () => void;
  onOpenWorkItem: (ticket: ProjectTicket) => void;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function ChatRoute({
  route,
  requestedSessionId,
  onOpenSession,
  onOpenChannel,
  onOpenLanding,
  onOpenWorkItem,
  onRouteReady
}: ChatRouteProps) {
  const chat = useProjectChat({ requestedSessionId, onSessionCreated: onOpenSession });
  const channels = useProjectChannels();
  const terminalAgentRunKey = chat.agentRun &&
    chat.agentRun.status !== 'Pending' &&
    chat.agentRun.status !== 'Running'
    ? `${chat.agentRun.agentRunId}:${chat.agentRun.status}`
    : null;
  const projectUnderstanding = useProjectUnderstanding({
    enabled: chat.conversationAuthorityEnabled,
    terminalAgentRunKey
  });

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

  if (chat.sessionLoadState === 'loading') {
    return (
      <main className="chat-route-workspace fl-route-loading" data-testid="chat.session.loading" aria-label={route.label}>
        Loading conversation...
      </main>
    );
  }

  if (chat.sessionLoadState === 'notFound') {
    return (
      <main className="chat-route-workspace" data-testid="chat.route" aria-label={route.label}>
        <RouteOutcomeScreen
          kind="notFound"
          title="Conversation not found"
          message={`Conversation ${requestedSessionId ?? ''} does not exist in this project or is no longer accessible.`}
          nextSafeAction="Open recent direct conversations and choose one returned by the backend."
          actionLabel="Open recent conversations"
          onAction={onOpenLanding}
        />
      </main>
    );
  }

  if (chat.sessionLoadState === 'unavailable') {
    return (
      <main className="chat-route-workspace" data-testid="chat.route" aria-label={route.label}>
        <RouteOutcomeScreen
          kind="unavailable"
          title="Conversations are unavailable"
          message={chat.sessionLoadError ?? 'The Workshop session API did not return conversation history.'}
          nextSafeAction="Retry the backend-owned session list. Your current URL has been preserved."
          actionLabel="Retry"
          onAction={chat.retrySessionLoad}
        />
      </main>
    );
  }

  return (
    <main className="chat-route-workspace" data-testid="chat.route" aria-label={route.label}>
      <ChatWorkspace
        sessions={chat.sessions}
        channels={channels.channels}
        canCreateChannels={channels.canCreateChannels}
        channelLoadState={channels.loadState}
        channelError={channels.error}
        activeSessionId={chat.sessionId}
        messages={chat.messages}
        composerValue={chat.draft}
        isSending={chat.isSending}
        isCancellingAgentRun={chat.isCancellingAgentRun}
        agentRun={chat.agentRun}
        conversationAuthorityEnabled={chat.conversationAuthorityEnabled}
        projectUnderstanding={projectUnderstanding}
        hasUnresolvedDurableOperation={chat.hasUnresolvedDurableOperation}
        agentCancellationDeliveryUnresolved={chat.agentCancellationDeliveryUnresolved}
        boundAgentRunChatSessionId={chat.boundAgentRunChatSessionId}
        disabledReason={chat.disabledReason}
        sendDisabledReason={chat.sendDisabledReason}
        documentSources={chat.documentSources}
        documentSourceLoadState={chat.documentSourceLoadState}
        documentSourceError={chat.documentSourceError}
        selectedDocumentSource={chat.selectedDocumentSource}
        errorMessage={chat.errorMessage}
        latestResponse={chat.latestResponse}
        latestResponseText={chat.latestResponseText}
        projectLabel={chat.projectLabel}
        onOpenSession={onOpenSession}
        onOpenChannel={onOpenChannel}
        onCreateChannel={channels.createChannel}
        onRetryChannels={channels.retry}
        onStartNewConversation={() => {
          if (chat.startNewConversation()) {
            onOpenLanding();
          }
        }}
        onComposerChange={chat.setDraft}
        onSend={chat.sendMessage}
        onCancelAgentRun={chat.cancelAgentRun}
        onLoadDocumentSources={chat.loadDocumentSources}
        onSelectDocumentSource={chat.setSelectedDocumentSource}
        onReviewProjectState={chat.reviewProjectState}
        onSaveDiscussion={chat.saveDiscussionFromMessage}
        onKeepDiscussingBaDraft={chat.keepDiscussingBaDraft}
        onAskNextBaQuestion={chat.askNextBaQuestion}
        onEditBaDraft={chat.editBaDraft}
        onCreateTicketFromDraft={chat.createTicketFromBaDraft}
        onOpenWorkItem={onOpenWorkItem}
      />
    </main>
  );
}
