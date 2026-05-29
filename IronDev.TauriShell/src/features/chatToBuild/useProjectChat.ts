import { useCallback, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ChatCompletionResponse } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { useWorkspaceNavigation } from '../../state/useWorkspaceNavigation';
import type { ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';

const projectReviewPrompt = [
  'Review the current project state for this project.',
  'Include current project state, recent tickets, recent decisions, recent runs, risks or blockers, and recommended next actions.',
  'Use grounded project context and call out missing context clearly.'
].join('\n');

export function useProjectChat() {
  const session = useSessionContext();
  const project = useProjectContext();
  const navigation = useWorkspaceNavigation();
  const [messages, setMessages] = useState<ChatWorkspaceMessage[]>([]);
  const [draft, setDraft] = useState('');
  const [isSending, setSending] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const projectId = project.selectedProjectId;
  const disabledReason = getChatBlockedReason(session.tokenConfigured, projectId, session.apiStatus.status, project.accessStatus);
  const sendDisabledReason = disabledReason ?? getChatSendBlockedReason(draft, isSending);
  const latestBuildableUserMessage = useMemo(
    () => [...messages].reverse().find((message) => message.role === 'user' && message.canContinueInBuild)?.content ?? '',
    [messages]
  );
  const buildBridgeContent = draft.trim() || latestBuildableUserMessage.trim();
  const buildBridgeDisabledReason = disabledReason ?? (buildBridgeContent ? null : 'Enter or send discussion text before continuing to Build.');
  const projectLabel = project.selectedProjectName ?? (projectId ? `Project ${projectId}` : 'Project required');

  const sendMessage = useCallback(
    async (request?: ChatSendRequest) => {
      const prompt = (request?.prompt ?? draft).trim();
      const displayText = request?.displayText ?? prompt;
      const blockedReason = disabledReason ?? (request ? (isSending ? 'Chat request is already sending.' : null) : getChatSendBlockedReason(draft, isSending));

      if (!projectId || blockedReason || !prompt) {
        return;
      }

      const userMessage: ChatWorkspaceMessage = {
        id: `user-${Date.now()}`,
        role: 'user',
        content: displayText,
        canContinueInBuild: request?.canContinueInBuild ?? true,
        createdUtc: new Date().toISOString()
      };

      setMessages((current) => [...current, userMessage]);
      setSending(true);
      setErrorMessage(null);

      try {
        const response = await session.client.completeChat(projectId, {
          projectId,
          prompt,
          sessionId: null,
          activeModel: null,
          mode: request?.mode ?? 'projectStateReview'
        });

        const assistantMessage: ChatWorkspaceMessage = {
          id: `assistant-${Date.now()}`,
          role: 'assistant',
          content: response.response?.trim() || 'IronDev.Api returned an empty response.',
          response,
          createdUtc: new Date().toISOString()
        };

        setMessages((current) => [...current, assistantMessage]);
        if (!request) {
          setDraft('');
        }
      } catch (error) {
        setErrorMessage(describeApiError(error, 'Send failed.'));
      } finally {
        setSending(false);
      }
    },
    [disabledReason, draft, isSending, projectId, session.client]
  );

  const reviewProjectState = useCallback(() => {
    void sendMessage({
      prompt: projectReviewPrompt,
      displayText: 'Review Project State',
      mode: 'projectStateReview',
      canContinueInBuild: false
    });
  }, [sendMessage]);

  const continueInBuild = useCallback(() => {
    const content = buildBridgeContent.trim();
    if (!content || disabledReason) {
      return;
    }

    navigation.setBuildDiscussionDraft({
      title: 'Project discussion',
      content,
      source: 'chat',
      createdUtc: new Date().toISOString()
    });
    navigation.navigateToWorkspace('build');
  }, [buildBridgeContent, disabledReason, navigation]);

  const latestResponse = useMemo(
    () => [...messages].reverse().find((message) => message.role === 'assistant' && message.response)?.response ?? null,
    [messages]
  );

  const latestResponseText = useMemo(
    () => [...messages].reverse().find((message) => message.role === 'assistant')?.content ?? null,
    [messages]
  );

  return {
    messages,
    draft,
    isSending,
    disabledReason,
    sendDisabledReason,
    buildBridgeDisabledReason,
    errorMessage,
    latestResponse: latestResponse as ChatCompletionResponse | null,
    latestResponseText,
    projectLabel,
    setDraft,
    sendMessage,
    reviewProjectState,
    continueInBuild
  };
}

function getChatBlockedReason(tokenConfigured: boolean, projectId: number | null, apiStatus: string, accessStatus: string) {
  if (!tokenConfigured) {
    return 'Authentication is required before chat can use project context.';
  }

  if (!projectId) {
    return 'Select a project before reviewing project state.';
  }

  if (apiStatus === 'loading') {
    return 'IronDev.Api connection is still being checked.';
  }

  if (apiStatus === 'disconnected' || apiStatus === 'error' || accessStatus === 'apiOffline') {
    return 'Backend chat service is unavailable.';
  }

  if (accessStatus === 'authInvalid') {
    return 'Authentication is invalid.';
  }

  return null;
}

function getChatSendBlockedReason(value: string, isSending: boolean) {
  if (isSending) {
    return 'Chat request is already sending.';
  }

  if (!value.trim()) {
    return 'Enter a message before sending.';
  }

  return null;
}

function describeApiError(error: unknown, fallback: string) {
  if (error instanceof IronDevApiError) {
    const body = error.body;
    if (body && typeof body === 'object' && 'error' in body && typeof body.error === 'string') {
      return `${fallback} ${body.error}`;
    }

    return `${fallback} HTTP ${error.status}.`;
  }

  return fallback;
}
