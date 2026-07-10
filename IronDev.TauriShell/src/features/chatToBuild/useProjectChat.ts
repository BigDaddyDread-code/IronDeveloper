import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  BaWorkingDraft,
  ChatClarificationKind,
  ChatCompletionResponse,
  ChatMessage,
  ChatTurnAuditResponse,
  ProjectChatSession,
  ProjectTicket
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { coerceChatGovernanceMode, getChatModeGate } from './chatGovernanceGate';
import { buildAssistantTagEnvelope, parseAssistantTagMetadata } from './chatTurnEnvelope';
import type { ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';

const projectReviewPrompt = [
  'Review the current project state for this project.',
  'Include current project state, recent tickets, recent decisions, recent runs, risks or blockers, and recommended next actions.',
  'Use grounded project context and call out missing context clearly.'
].join('\n');

const chatAuditHydrationLimit = 50;

type SessionLoadState = 'loading' | 'ready' | 'notFound' | 'unavailable';

interface UseProjectChatOptions {
  requestedSessionId: number | null;
  onSessionCreated: (sessionId: number) => void;
}

export function useProjectChat({ requestedSessionId, onSessionCreated }: UseProjectChatOptions) {
  const session = useSessionContext();
  const project = useProjectContext();
  const [sessions, setSessions] = useState<ProjectChatSession[]>([]);
  const [messages, setMessages] = useState<ChatWorkspaceMessage[]>([]);
  const [draft, setDraft] = useState('');
  const [isSending, setSending] = useState(false);
  const [isHistoryLoading, setHistoryLoading] = useState(false);
  const [sessionId, setSessionId] = useState<number | null>(null);
  const [sessionLoadState, setSessionLoadState] = useState<SessionLoadState>('loading');
  const [sessionLoadRequest, setSessionLoadRequest] = useState(0);
  const [sessionLoadError, setSessionLoadError] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const freshConversationRef = useRef(false);
  const errorSessionIdRef = useRef<number | null>(null);

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
        setSessions([]);
        setMessages([]);
        setSessionId(null);
        setSessionLoadState('ready');
        setHistoryLoading(false);
        return;
      }

      setHistoryLoading(true);
      setSessionLoadState('loading');
      setSessionLoadError(null);
      if (requestedSessionId !== null) {
        freshConversationRef.current = false;
      }

      try {
        const recentSessions = await session.client.getProjectChatSessions(projectId);
        let availableSessions = recentSessions.filter((item) => Number.isFinite(item.id));
        let targetSession = requestedSessionId
          ? availableSessions.find((item) => item.id === requestedSessionId)
          : availableSessions[0];

        if (requestedSessionId && !targetSession) {
          const requestedSession = await session.client.getProjectChatSession(
            projectId,
            requestedSessionId
          );
          if (!requestedSession?.id || requestedSession.projectId !== projectId) {
            if (!isCancelled) {
              setSessions(availableSessions);
              setMessages([]);
              setSessionId(null);
              setSessionLoadState('notFound');
            }
            return;
          }
          targetSession = requestedSession;
          availableSessions = [requestedSession, ...availableSessions];
        }

        if (!isCancelled) {
          setSessions(availableSessions);
        }

        if (freshConversationRef.current && requestedSessionId === null) {
          if (!isCancelled) {
            setMessages([]);
            setSessionId(null);
            setSessionLoadState('ready');
          }
          return;
        }

        if (!targetSession?.id) {
          if (!isCancelled) {
            setMessages([]);
            setSessionId(null);
            setSessionLoadState('ready');
            if (errorSessionIdRef.current !== null) {
              errorSessionIdRef.current = null;
              setErrorMessage(null);
            }
          }
          return;
        }

        if (errorSessionIdRef.current !== null && errorSessionIdRef.current !== targetSession.id) {
          errorSessionIdRef.current = null;
          setErrorMessage(null);
        }

        const history = await session.client.getProjectChatMessages(projectId, targetSession.id);
        const replayedMessages = history.map(mapApiMessage).filter(Boolean);
        const hydratedMessages = await hydrateMessagesWithDurableAudit(
          projectId,
          targetSession.id,
          history,
          replayedMessages,
          session.client
        );
        if (!isCancelled) {
          setSessionId(targetSession.id);
          setMessages(hydratedMessages);
          setSessionLoadState('ready');
        }
      } catch (error) {
        if (!isCancelled) {
          setMessages([]);
          setSessionId(null);
          setSessionLoadState(
            requestedSessionId && error instanceof IronDevApiError && error.status === 404
              ? 'notFound'
              : 'unavailable'
          );
          setSessionLoadError(describeApiError(error, 'Chat history failed to load.'));
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
  }, [
    projectId,
    requestedSessionId,
    session.apiStatus.status,
    session.client,
    session.tokenConfigured,
    sessionLoadRequest
  ]);

  const retrySessionLoad = useCallback(() => {
    setSessionLoadRequest((current) => current + 1);
  }, []);

  const startNewConversation = useCallback(() => {
    freshConversationRef.current = true;
    setSessionId(null);
    setMessages([]);
    setDraft('');
    errorSessionIdRef.current = null;
    setErrorMessage(null);
    setSessionLoadState('ready');
  }, []);

  const ensureChatSession = useCallback(
    async (prompt: string) => {
      if (!projectId) {
        throw new Error('Project is required before creating a chat session.');
      }

      if (sessionId) {
        return { id: sessionId, created: false };
      }

      const title = createSessionTitle(prompt);
      const createdSessionId = await session.client.saveProjectChatSession(projectId, {
        projectId,
        title,
        summary: 'Project conversation'
      });
      freshConversationRef.current = false;
      setSessionId(createdSessionId);
      return { id: createdSessionId, created: true };
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
      errorSessionIdRef.current = null;
      setErrorMessage(null);
      let createdSessionId: number | null = null;

      try {
        const activeSession = await ensureChatSession(prompt);
        const activeSessionId = activeSession.id;
        createdSessionId = activeSession.created ? activeSessionId : null;
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
        const responseMode = coerceChatGovernanceMode(response.mode);
        const responseGate = getChatModeGate({ ...response, mode: responseMode });
        const responseWithGate = { ...response, mode: responseMode, gate: responseGate };
        const savedAssistantTags = buildAssistantTagEnvelope(responseWithGate, responseMode);

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
            modeConfidence: response.modeConfidence ?? null,
            modeReason: response.modeReason ?? null,
            clarification: response.clarification ?? null,
            gate: responseGate,
            reasoningTrace: response.reasoningTrace ?? [],
            disambiguationQuestion: response.disambiguationQuestion ?? null,
            reasoningSummary: response.reasoningSummary ?? null,
            dogfoodTraceId: response.dogfoodTraceId ?? null,
            dogfoodTracePath: response.dogfoodTracePath ?? null,
            routeTraceId: response.routeTraceId ?? null,
            routeSource: response.routeSource ?? null,
            routeChallenge: response.routeChallenge ?? null,
            baDraft: response.baDraft ?? null,
            auditSource: 'live',
            auditFallbackReason: null,
            auditHasFallbackEvidence: false,
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
        errorSessionIdRef.current = createdSessionId ?? sessionId;
        setErrorMessage(describeApiError(error, 'Send failed.'));
      } finally {
        setSending(false);
        if (createdSessionId) {
          onSessionCreated(createdSessionId);
        }
      }
    },
    [disabledReason, draft, ensureChatSession, isSending, onSessionCreated, projectId, session.client, sessionId]
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

  const keepDiscussingBaDraft = useCallback(() => {
    setDraft('');
  }, []);

  const askNextBaQuestion = useCallback(
    (baDraft: BaWorkingDraft) => {
      const title = baDraft.candidateTitle?.trim() || 'the current BA draft';
      const question = baDraft.openQuestions?.find(Boolean);
      void sendMessage({
        prompt: question
          ? `Ask the next useful BA question for "${title}". Current top open question: ${question}`
          : `Ask the next useful BA question for "${title}".`,
        displayText: 'Ask next question',
        mode: 'projectQuestion',
        canContinueInBuild: true
      });
    },
    [sendMessage]
  );

  const editBaDraft = useCallback((baDraft: BaWorkingDraft) => {
    setDraft(formatBaDraftForComposer(baDraft));
  }, []);

  const createTicketFromBaDraft = useCallback(
    async (baDraft: BaWorkingDraft): Promise<ProjectTicket> => {
      if (!projectId || !sessionId) {
        throw new Error('Create ticket failed. A project Chat session is required.');
      }

      let ticket: ProjectTicket;
      try {
        ticket = await session.client.confirmBaWorkingDraft(projectId, {
          sourceChatSessionId: sessionId,
          draft: baDraft
        });
      } catch (error) {
        throw new Error(describeApiError(error, 'Create ticket failed.'));
      }
      if (!ticket.id) {
        throw new Error('Create ticket failed. Backend response did not include a ticket identifier.');
      }
      return ticket;
    },
    [projectId, session.client, sessionId]
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
    sessions,
    sessionId,
    sessionLoadState,
    sessionLoadError,
    messages,
    draft,
    isSending,
    disabledReason,
    sendDisabledReason,
    errorMessage,
    latestResponse: latestResponse as ChatCompletionResponse | null,
    latestResponseText,
    projectLabel,
    retrySessionLoad,
    startNewConversation,
    setDraft,
    sendMessage,
    reviewProjectState,
    saveDiscussionFromMessage,
    keepDiscussingBaDraft,
    askNextBaQuestion,
    editBaDraft,
    createTicketFromBaDraft
  };
}

function formatBaDraftForComposer(baDraft: BaWorkingDraft) {
  const sections = [
    `Title: ${baDraft.candidateTitle ?? ''}`,
    section('Problem', baDraft.problem),
    section('Proposed change', baDraft.proposedChange),
    listSection('Rules', baDraft.businessRules),
    listSection('Acceptance criteria', baDraft.acceptanceCriteria),
    listSection('Assumptions', baDraft.assumptions),
    listSection('Open questions', baDraft.openQuestions)
  ].filter(Boolean);

  return sections.join('\n\n');
}

function section(title: string, value: string | null | undefined) {
  return value?.trim() ? `${title}:\n${value.trim()}` : '';
}

function listSection(title: string, values: string[] | null | undefined) {
  const items = values?.filter(Boolean) ?? [];
  if (items.length === 0) {
    return '';
  }

  return `${title}:\n${items.map((item) => `- ${item}`).join('\n')}`;
}

function mapApiMessage(message: ChatMessage): ChatWorkspaceMessage {
  const role = message.role === 'assistant' ? 'assistant' : 'user';
  const content = message.message?.trim() || '';
  const metadata = parseAssistantTagMetadata(message.tags);
  const responseMode = metadata.mode ?? null;
  const hasTagReplay = hasAssistantTagReplayMetadata(metadata);

  return {
    id: `${role}-${message.id ?? `${message.chatSessionId ?? 'local'}-${Date.now()}`}`,
    role,
    content,
    canContinueInBuild: role === 'user',
    createdUtc: message.createdDate ?? new Date().toISOString(),
    response: role === 'assistant'
        ? {
          response: content,
          modeConfidence: metadata.modeConfidence ?? null,
          modeReason: metadata.modeReason ?? null,
          clarification: metadata.clarification ?? null,
          gate: metadata.gate ?? null,
          contextSummary: metadata.contextSummary ?? message.contextSummary ?? null,
          linkedFilePaths: metadata.linkedFilePaths ?? message.linkedFilePaths ?? null,
          linkedSymbols: metadata.linkedSymbols ?? message.linkedSymbols ?? null,
          traceId: metadata.traceId ?? null,
          mode: responseMode,
          reasoningTrace: metadata.reasoningTrace ?? [],
          disambiguationQuestion: metadata.disambiguationQuestion ?? null,
          reasoningSummary: metadata.reasoningSummary ?? null,
          dogfoodTraceId: metadata.dogfoodTraceId ?? null,
          dogfoodTracePath: metadata.dogfoodTracePath ?? null,
          routeTraceId: null,
          routeSource: metadata.routeSource ?? null,
          routeChallenge: metadata.routeChallenge ?? null,
          baDraft: metadata.baDraft ?? null,
          auditSource: hasTagReplay ? 'tags' : 'none',
          auditFallbackReason: hasTagReplay
            ? 'Durable audit row was unavailable; restored from ChatMessage.Tags replay envelope.'
            : null,
          auditHasFallbackEvidence: false
        }
      : null
  };
}

async function hydrateMessagesWithDurableAudit(
  projectId: number,
  sessionId: number,
  history: ChatMessage[],
  mappedMessages: ChatWorkspaceMessage[],
  client: ReturnType<typeof useSessionContext>['client']
) {
  // Slice 4 keeps replay hydration bounded to the current history page. The follow-up is
  // CHAT-AUDIT-BATCH-001: replace this with one session-scoped batch audit endpoint.
  const auditHydrationTargets = new Set(
    mappedMessages
      .map((message, index) => ({ message, index }))
      .filter(({ message }) => message.role === 'assistant')
      .slice(-chatAuditHydrationLimit)
      .map(({ index }) => index)
  );

  return Promise.all(
    mappedMessages.map(async (mappedMessage, index) => {
      const apiMessage = history[index];
      if (mappedMessage.role !== 'assistant' || !apiMessage?.id || !auditHydrationTargets.has(index)) {
        return mappedMessage;
      }

      try {
        const audit = await client.getProjectChatMessageAudit(projectId, sessionId, apiMessage.id);
        return applyDurableAudit(mappedMessage, audit);
      } catch (error) {
        if (error instanceof IronDevApiError && error.status === 404) {
          return mappedMessage;
        }

        return {
          ...mappedMessage,
          response: mappedMessage.response
            ? {
                ...mappedMessage.response,
                auditFallbackReason: 'Durable audit lookup failed; showing replay metadata if available.'
              }
            : mappedMessage.response
        };
      }
    })
  );
}

function applyDurableAudit(message: ChatWorkspaceMessage, audit: ChatTurnAuditResponse): ChatWorkspaceMessage {
  const mode = coerceChatGovernanceMode(audit.mode);
  const clarification = normalizeAuditClarification(audit.clarification);
  const gate = getChatModeGate({
    mode,
    modeConfidence: audit.modeConfidence,
    modeReason: audit.modeReason,
    gate: audit.gate
  });
  const disambiguationQuestion = clarification?.required
    ? clarification.questions?.[0] ?? null
    : null;

  return {
    ...message,
    response: {
      ...(message.response ?? { response: message.content }),
      response: message.content,
      mode,
      modeConfidence: audit.modeConfidence,
      modeReason: audit.modeReason,
      clarification,
      gate,
      contextSummary: audit.contextSummary ?? null,
      linkedFilePaths: audit.linkedFilePaths ?? null,
      linkedSymbols: audit.linkedSymbols ?? null,
      dogfoodTraceId: audit.dogfoodTraceId ?? null,
      dogfoodTracePath: null,
      routeTraceId: audit.routeTraceId ?? null,
      routeSource: audit.routeSource ?? null,
      routeChallenge: audit.routeChallenge ?? null,
      baDraft: audit.baDraft ?? null,
      traceId: null,
      reasoningTrace: buildDurableAuditTrace(audit),
      disambiguationQuestion,
      reasoningSummary: `Durable audit replay restored ${mode ?? 'unknown'} mode, clarification, gate, and trace pointers without backend recompute.`,
      auditSource: 'durable',
      auditFallbackReason: null,
      auditHasFallbackEvidence: audit.isFallbackEvidence
    }
  };
}

function buildDurableAuditTrace(audit: ChatTurnAuditResponse) {
  const clarification = normalizeAuditClarification(audit.clarification);
  return [
    `Durable audit source: ${audit.source}.`,
    `Mode: ${audit.mode} (${Math.round(audit.modeConfidence * 100)}%).`,
    `Mode reason: ${audit.modeReason}`,
    audit.routeSource ? `Effective route source: ${audit.routeSource}` : 'Effective route source: unknown.',
    audit.routeChallenge
      ? `Route challenge: ${audit.routeChallenge.suggestedMode ?? 'unknown'} / ${audit.routeChallenge.suggestedRequestKind ?? 'unknown'} (${Math.round((audit.routeChallenge.confidence ?? 0) * 100)}%) - ${audit.routeChallenge.reason ?? 'No reason recorded.'}`
      : 'Route challenge: none.',
    audit.baDraft
      ? `BA draft: ${audit.baDraft.candidateTitle ?? 'Untitled draft'} (${Math.round((audit.baDraft.confidence ?? 0) * 100)}%).`
      : 'BA draft: none.',
    clarification?.required
      ? `Clarification: ${clarification.kind} - ${(clarification.questions ?? []).join(' | ')}`
      : 'Clarification: none required.',
    `Gate: save=${Boolean(audit.gate?.canSaveDiscussion)}; ticket=${Boolean(audit.gate?.canCreateTicket)}; sources=${Boolean(audit.gate?.canViewSources)}; copy=${Boolean(audit.gate?.canCopyMarkdown)}.`,
    audit.routeTraceId ? `Route trace id: ${audit.routeTraceId}` : 'Route trace id: none.',
    audit.dogfoodTraceId ? `Dogfood trace id: ${audit.dogfoodTraceId}` : 'Dogfood trace id: none.',
    audit.isFallbackEvidence ? 'Fallback evidence: present.' : 'Fallback evidence: none.'
  ];
}

function normalizeAuditClarification(clarification: ChatTurnAuditResponse['clarification'] | null | undefined) {
  if (!clarification) {
    return null;
  }

  return {
    ...clarification,
    kind: coerceChatClarificationKind(clarification.kind)
  };
}

function coerceChatClarificationKind(value: ChatClarificationKind | number | string | null | undefined): ChatClarificationKind {
  if (value === 0) {
    return 'None';
  }

  if (value === 1) {
    return 'GeneralScope';
  }

  if (value === 2) {
    return 'ProductScope';
  }

  if (value === 3) {
    return 'MissingProjectContext';
  }

  if (value === 4) {
    return 'GovernanceIntent';
  }

  if (value === 5) {
    return 'SafetyOrRisk';
  }

  const normalized = String(value ?? '').trim();
  if (
    normalized === 'GeneralScope' ||
    normalized === 'ProductScope' ||
    normalized === 'MissingProjectContext' ||
    normalized === 'GovernanceIntent' ||
    normalized === 'SafetyOrRisk'
  ) {
    return normalized;
  }

  return 'None';
}

function hasAssistantTagReplayMetadata(metadata: ReturnType<typeof parseAssistantTagMetadata>) {
  return Boolean(
    metadata.mode ||
    metadata.gate ||
    metadata.modeReason ||
    metadata.clarification ||
    metadata.reasoningTrace?.length ||
    metadata.reasoningSummary ||
    metadata.baDraft ||
    metadata.contextSummary ||
    metadata.linkedFilePaths ||
    metadata.linkedSymbols
  );
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
