import { useCallback, useEffect, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ChatCompletionResponse, ChatMessage } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import type { ChatResponseMode, ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';

const projectReviewPrompt = [
  'Review the current project state for this project.',
  'Include current project state, recent tickets, recent decisions, recent runs, risks or blockers, and recommended next actions.',
  'Use grounded project context and call out missing context clearly.'
].join('\n');
const chatMessageTagVersion = 1;

export function useProjectChat() {
  const session = useSessionContext();
  const project = useProjectContext();
  const [messages, setMessages] = useState<ChatWorkspaceMessage[]>([]);
  const [draft, setDraft] = useState('');
  const [isSending, setSending] = useState(false);
  const [isHistoryLoading, setHistoryLoading] = useState(false);
  const [sessionId, setSessionId] = useState<number | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const projectId = project.selectedProjectId;
  const disabledReason =
    getChatBlockedReason(session.tokenConfigured, projectId, session.apiStatus.status, project.accessStatus) ??
    (isHistoryLoading ? 'Chat history is loading.' : null);
  const sendDisabledReason = disabledReason ?? getChatSendBlockedReason(draft, isSending);
  const projectLabel = project.selectedProjectName ?? (projectId ? `Project ${projectId}` : 'Project required');

  useEffect(() => {
    let isCancelled = false;

    async function loadProjectChat() {
      if (!projectId || !session.tokenConfigured || session.apiStatus.status !== 'connected') {
        setMessages([]);
        setSessionId(null);
        setHistoryLoading(false);
        return;
      }

      setHistoryLoading(true);
      setErrorMessage(null);

      try {
        const sessions = await session.client.getProjectChatSessions(projectId);
        const latestSession = sessions.find((item) => Number.isFinite(item.id));

        if (!latestSession?.id) {
          if (!isCancelled) {
            setMessages([]);
            setSessionId(null);
          }
          return;
        }

        const history = await session.client.getProjectChatMessages(projectId, latestSession.id);
        if (!isCancelled) {
          setSessionId(latestSession.id);
          setMessages(history.map(mapApiMessage).filter(Boolean));
        }
      } catch (error) {
        if (!isCancelled) {
          setMessages([]);
          setSessionId(null);
          setErrorMessage(describeApiError(error, 'Chat history failed to load.'));
        }
      } finally {
        if (!isCancelled) {
          setHistoryLoading(false);
        }
      }
    }

    void loadProjectChat();

    return () => {
      isCancelled = true;
    };
  }, [projectId, session.apiStatus.status, session.client, session.tokenConfigured]);

  const ensureChatSession = useCallback(
    async (prompt: string) => {
      if (!projectId) {
        throw new Error('Project is required before creating a chat session.');
      }

      if (sessionId) {
        return sessionId;
      }

      const title = createSessionTitle(prompt);
      const createdSessionId = await session.client.saveProjectChatSession(projectId, {
        projectId,
        title,
        summary: 'Project conversation'
      });
      setSessionId(createdSessionId);
      return createdSessionId;
    },
    [projectId, session.client, sessionId]
  );

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
        const activeSessionId = await ensureChatSession(prompt);
        const savedUserMessageId = await session.client.saveProjectChatMessage(projectId, activeSessionId, {
          projectId,
          chatSessionId: activeSessionId,
          role: 'user',
          message: displayText,
          tags: request?.mode ?? 'projectQuestion'
        });

        setMessages((current) =>
          current.map((message) =>
            message.id === userMessage.id
              ? { ...message, id: `user-${savedUserMessageId}` }
              : message
          )
        );

        const response = await session.client.completeChat(projectId, {
          projectId,
          prompt,
          sessionId: activeSessionId,
          activeModel: null,
          mode: request?.mode ?? 'projectQuestion'
        });
        const responseMode = coerceResponseMode(response.mode) ?? (response.showGovernanceActions ? 'Formalization' : null);
        const savedAssistantTags = buildAssistantTagEnvelope(response, responseMode);

        const savedAssistantMessageId = await session.client.saveProjectChatMessage(projectId, activeSessionId, {
          projectId,
          chatSessionId: activeSessionId,
          role: 'assistant',
          message: response.response?.trim() || 'IronDev.Api returned an empty response.',
          tags: savedAssistantTags,
          contextSummary: response.contextSummary ?? null,
          linkedFilePaths: response.linkedFilePaths ?? null,
          linkedSymbols: response.linkedSymbols ?? null
        });

        const assistantMessage: ChatWorkspaceMessage = {
          id: `assistant-${savedAssistantMessageId}`,
          role: 'assistant',
          content: response.response?.trim() || 'IronDev.Api returned an empty response.',
          response: {
            ...response,
            response: response.response?.trim() || 'IronDev.Api returned an empty response.',
            mode: responseMode,
            showGovernanceActions: response.showGovernanceActions ?? false,
            governanceActions: response.governanceActions ?? null,
            reasoningTrace: response.reasoningTrace ?? null,
            disambiguationQuestion: response.disambiguationQuestion ?? null,
            reasoningSummary: response.reasoningSummary ?? null,
            dogfoodTraceId: response.dogfoodTraceId ?? null,
            dogfoodTracePath: response.dogfoodTracePath ?? null,
            linkedFilePaths: response.linkedFilePaths ?? null,
            linkedSymbols: response.linkedSymbols ?? null,
            contextSummary: response.contextSummary ?? null,
            traceId: response.traceId ?? null
          },
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
    [disabledReason, draft, ensureChatSession, isSending, projectId, session.client]
  );

  const reviewProjectState = useCallback(() => {
    void sendMessage({
      prompt: projectReviewPrompt,
      displayText: 'Review Project State',
      mode: 'projectStateReview',
      canContinueInBuild: false
    });
  }, [sendMessage]);

  const saveDiscussionFromMessage = useCallback(
    async (messageId: string) => {
      if (!projectId || disabledReason) {
        return;
      }

      const message = messages.find((item) => item.id === messageId);
      if (!message || message.role !== 'assistant' || !message.content.trim()) {
        return;
      }

      setMessages((current) =>
        current.map((item) =>
          item.id === messageId
            ? { ...item, discussionSaveStatus: 'saving', discussionSaveError: null }
            : item
        )
      );

      try {
        const response = await session.client.saveDiscussion(projectId, {
          title: createSessionTitle(message.content),
          content: message.content
        });

        setMessages((current) =>
          current.map((item) =>
            item.id === messageId
              ? {
                  ...item,
                  discussionSaveStatus: 'saved',
                  discussionSaveError: null,
                  savedDiscussion: {
                    documentId: response.documentId,
                    documentVersionId: response.documentVersionId
                  }
                }
              : item
          )
        );
      } catch (error) {
        setMessages((current) =>
          current.map((item) =>
            item.id === messageId
              ? {
                  ...item,
                  discussionSaveStatus: 'error',
                  discussionSaveError: describeApiError(error, 'Save Discussion failed.')
                }
              : item
          )
        );
      }
    },
    [disabledReason, messages, projectId, session.client]
  );

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
    errorMessage,
    latestResponse: latestResponse as ChatCompletionResponse | null,
    latestResponseText,
    projectLabel,
    setDraft,
    sendMessage,
    reviewProjectState,
    saveDiscussionFromMessage
  };
}

function mapApiMessage(message: ChatMessage): ChatWorkspaceMessage {
  const role = message.role === 'assistant' ? 'assistant' : 'user';
  const content = message.message?.trim() || '';
  const metadata = parseAssistantTagMetadata(message.tags);
  const responseMode =
    metadata.mode ??
    coerceResponseMode(message.tags) ??
    coerceLegacyModeToResponseMode(message.tags);

  return {
    id: `${role}-${message.id ?? `${message.chatSessionId ?? 'local'}-${Date.now()}`}`,
    role,
    content,
    canContinueInBuild: role === 'user',
    createdUtc: message.createdDate ?? new Date().toISOString(),
    response: role === 'assistant'
      ? {
          response: content,
          contextSummary: metadata.contextSummary ?? message.contextSummary ?? null,
          linkedFilePaths: metadata.linkedFilePaths ?? message.linkedFilePaths ?? null,
          linkedSymbols: metadata.linkedSymbols ?? message.linkedSymbols ?? null,
          traceId: metadata.traceId ?? null,
          mode: responseMode,
          showGovernanceActions: metadata.showGovernanceActions ?? false,
          governanceActions: metadata.governanceActions ?? null,
          reasoningTrace: metadata.reasoningTrace ?? [],
          disambiguationQuestion: metadata.disambiguationQuestion ?? null,
          reasoningSummary: metadata.reasoningSummary ?? null,
          dogfoodTraceId: metadata.dogfoodTraceId ?? null,
          dogfoodTracePath: metadata.dogfoodTracePath ?? null
        }
      : null
  };
}

function buildAssistantTagEnvelope(response: ChatCompletionResponse, mode: ChatResponseMode | null) {
  return JSON.stringify({
    v: chatMessageTagVersion,
    mode,
    showGovernanceActions: response.showGovernanceActions ?? false,
    governanceActions: response.governanceActions,
    reasoningTrace: response.reasoningTrace ?? [],
    disambiguationQuestion: response.disambiguationQuestion,
    reasoningSummary: response.reasoningSummary,
    dogfoodTraceId: response.dogfoodTraceId,
    dogfoodTracePath: response.dogfoodTracePath,
    traceId: response.traceId,
    contextSummary: response.contextSummary,
    linkedFilePaths: response.linkedFilePaths,
    linkedSymbols: response.linkedSymbols
  });
}

function parseAssistantTagMetadata(rawTags: string | null | undefined) {
  if (!rawTags) {
    return {} as AssistantTagMetadata;
  }

  try {
  const parsed = JSON.parse(rawTags) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return {} as AssistantTagMetadata;
    }

    const record = parsed as Record<string, unknown>;
    const version = toNumber(record.v);
    if (version !== chatMessageTagVersion) {
      return {} as AssistantTagMetadata;
    }
    const mode = coerceResponseMode(typeof record.mode === 'string' ? record.mode : undefined);
    const showGovernanceActions = typeof record.showGovernanceActions === 'boolean'
      ? record.showGovernanceActions
      : undefined;
    const reasoningTrace = toStringList(record.reasoningTrace);
    const governanceActions = toStringList(record.governanceActions);
    const disambiguationQuestion = typeof record.disambiguationQuestion === 'string'
      ? record.disambiguationQuestion
      : null;
    const reasoningSummary = typeof record.reasoningSummary === 'string'
      ? record.reasoningSummary
      : null;
    const dogfoodTraceId = typeof record.dogfoodTraceId === 'string'
      ? record.dogfoodTraceId
      : null;
    const dogfoodTracePath = typeof record.dogfoodTracePath === 'string'
      ? record.dogfoodTracePath
      : null;
    const traceId = toNumber(record.traceId);
    const contextSummary = typeof record.contextSummary === 'string'
      ? record.contextSummary
      : null;
    const linkedFilePaths = typeof record.linkedFilePaths === 'string'
      ? record.linkedFilePaths
      : null;
    const linkedSymbols = typeof record.linkedSymbols === 'string'
      ? record.linkedSymbols
      : null;

    return {
      mode,
      showGovernanceActions,
      reasoningTrace,
      governanceActions,
      disambiguationQuestion,
      reasoningSummary,
      dogfoodTraceId,
      dogfoodTracePath,
      traceId,
      contextSummary,
      linkedFilePaths,
      linkedSymbols
    } as AssistantTagMetadata;
  } catch {
    return {} as AssistantTagMetadata;
  }
}

interface AssistantTagMetadata {
  mode?: ChatResponseMode | null;
  showGovernanceActions?: boolean;
  governanceActions?: string[];
  reasoningTrace?: string[];
  disambiguationQuestion?: string | null;
  reasoningSummary?: string | null;
  dogfoodTraceId?: string | null;
  dogfoodTracePath?: string | null;
  traceId?: number | null;
  contextSummary?: string | null;
  linkedFilePaths?: string | null;
  linkedSymbols?: string | null;
}

function coerceResponseMode(value?: string | null): ChatResponseMode | null {
  if (!value) {
    return null;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === 'exploration') {
    return 'Exploration';
  }

  if (normalized === 'formalization') {
    return 'Formalization';
  }

  if (normalized === 'confirmation') {
    return 'Confirmation';
  }

  return null;
}

function coerceLegacyModeToResponseMode(value?: string | null): ChatResponseMode | null {
  if (!value) {
    return null;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === 'projectquestion') {
    return null;
  }

  if (normalized === 'projectexploration' || normalized === 'exploration') {
    return 'Exploration';
  }

  if (normalized === 'projectformalization' || normalized === 'formalization') {
    return 'Formalization';
  }

  if (normalized === 'projectconfirmation' || normalized === 'confirmation') {
    return 'Confirmation';
  }

  return null;
}

function toNumber(value: unknown) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === 'string') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function toStringList(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .filter((item): item is string => typeof item === 'string')
    .map((item) => item.trim())
    .filter(Boolean);
}

function createSessionTitle(prompt: string) {
  const normalized = prompt.replace(/\s+/g, ' ').trim();
  if (!normalized) {
    return 'Project conversation';
  }

  return normalized.length > 80 ? `${normalized.slice(0, 77)}...` : normalized;
}

function getChatBlockedReason(tokenConfigured: boolean, projectId: number | null, apiStatus: string, accessStatus: string) {
  if (!tokenConfigured) {
    return 'Authentication is required before chat can use project context.';
  }

  if (!projectId) {
    return 'Select a project before chatting with IronDev.';
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
