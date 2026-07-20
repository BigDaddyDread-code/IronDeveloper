import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  BaWorkingDraft,
  ChatClarificationKind,
  ChatCompletionResponse,
  ChatDocumentSource,
  ChatMessage,
  ChatTurnAuditResponse,
  ProjectChatSession,
  ProjectTicket,
  WorkbenchAgentRunSnapshot,
  WorkbenchAgentRunStatus
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { coerceChatGovernanceMode, getChatModeGate } from './chatGovernanceGate';
import { buildAssistantTagEnvelope, parseAssistantTagMetadata } from './chatTurnEnvelope';
import type { ChatAgentRunState, ChatSendRequest, ChatWorkspaceMessage } from './chatTypes';

const projectReviewPrompt = [
  'Review the current project state for this project.',
  'Include current project state, recent tickets, recent decisions, recent runs, risks or blockers, and recommended next actions.',
  'Use grounded project context and call out missing context clearly.'
].join('\n');

const chatAuditHydrationLimit = 50;

type SessionLoadState = 'loading' | 'ready' | 'notFound' | 'unavailable';
type DocumentSourceLoadState = 'idle' | 'loading' | 'ready' | 'error';
type AgentRunAvailability = 'unknown' | 'checking' | 'available' | 'unavailable';

interface DurableOperationAttempt {
  key: string;
  clientOperationId: string;
}

interface UnresolvedDurableOperation {
  prompt: string;
  kind: 'CreateSession' | 'SubmitRun';
}

class StaleWorkbenchSubmissionContextError extends Error {}

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
  const [isCancellingAgentRun, setCancellingAgentRun] = useState(false);
  const [agentRun, setAgentRun] = useState<ChatAgentRunState | null>(null);
  const [agentRunAvailability, setAgentRunAvailability] = useState<AgentRunAvailability>('unknown');
  const [agentRunUnavailableCategory, setAgentRunUnavailableCategory] = useState<string | null>(null);
  const [boundAgentRunChatSessionId, setBoundAgentRunChatSessionId] = useState<number | null>(null);
  const [isHistoryLoading, setHistoryLoading] = useState(false);
  const [sessionId, setSessionId] = useState<number | null>(null);
  const [sessionLoadState, setSessionLoadState] = useState<SessionLoadState>('loading');
  const [sessionLoadRequest, setSessionLoadRequest] = useState(0);
  const [sessionLoadError, setSessionLoadError] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [documentSources, setDocumentSources] = useState<ChatDocumentSource[]>([]);
  const [documentSourceLoadState, setDocumentSourceLoadState] = useState<DocumentSourceLoadState>('idle');
  const [documentSourceError, setDocumentSourceError] = useState<string | null>(null);
  const [selectedDocumentSource, setSelectedDocumentSource] = useState<ChatDocumentSource | null>(null);
  const [unresolvedDurableOperations, setUnresolvedDurableOperations] = useState<Record<string, UnresolvedDurableOperation>>({});
  const freshConversationRef = useRef(false);
  const errorSessionIdRef = useRef<number | null>(null);
  const agentSubmissionAttemptsRef = useRef(new Map<string, DurableOperationAttempt>());
  const chatSessionCreationAttemptsRef = useRef(new Map<string, DurableOperationAttempt>());
  const agentCancellationAttemptsRef = useRef(new Map<string, DurableOperationAttempt>());
  const agentPollControllerRef = useRef<AbortController | null>(null);
  const agentSubmissionInFlightRef = useRef(false);
  const agentSubmissionGenerationRef = useRef(0);
  const agentTerminalHistoryRefreshRef = useRef<string | null>(null);

  const projectId = project.selectedProjectId;
  const workbenchSession = project.workbenchSession;
  const authorityContextKey = projectId && workbenchSession
    ? `${projectId}:${workbenchSession.workbenchSessionId}:${workbenchSession.leaseEpoch}`
    : null;
  const authorityContextKeyRef = useRef<string | null>(authorityContextKey);
  authorityContextKeyRef.current = authorityContextKey;
  const conversationAuthorityEnabled =
    session.environmentInfo?.workbench?.conversationAuthorityEnabled === true;
  const unresolvedDurableOperation = authorityContextKey
    ? unresolvedDurableOperations[authorityContextKey] ?? null
    : null;
  const hasUnresolvedDurableOperation = unresolvedDurableOperation !== null;
  const agentRunIsActive = isActiveAgentRunStatus(agentRun?.status);
  const agentRunUnavailableReason =
    conversationAuthorityEnabled && agentRunAvailability === 'unavailable' && !agentRunIsActive
      ? describeAgentRunUnavailable(agentRunUnavailableCategory)
      : null;
  const agentRunReadinessReason =
    conversationAuthorityEnabled && (agentRunAvailability === 'unknown' || agentRunAvailability === 'checking') && !agentRunIsActive
      ? 'Business Analyst readiness is being checked.'
      : agentRunUnavailableReason;
  const disabledReason =
    getChatBlockedReason(session.tokenConfigured, projectId, workbenchSession !== null, session.apiStatus.status, project.accessStatus) ??
    (isHistoryLoading ? 'Workshop history is loading.' : null);
  const sendDisabledReason =
    disabledReason ??
    agentRunReadinessReason ??
    (unresolvedDurableOperation && draft.trim() !== unresolvedDurableOperation.prompt
      ? 'Delivery is unresolved. Restore the unchanged message to retry the same operation safely.'
      : null) ??
    getChatSendBlockedReason(draft, isSending);
  const projectLabel = project.selectedProjectName ?? (projectId ? `Project ${projectId}` : 'Project required');

  const markDurableOperationUnresolved = useCallback((
    contextKey: string,
    prompt: string,
    kind: UnresolvedDurableOperation['kind']
  ) => {
    setUnresolvedDurableOperations((current) => ({
      ...current,
      [contextKey]: { prompt, kind }
    }));
  }, []);

  const clearDurableOperationUnresolved = useCallback((contextKey: string, prompt: string) => {
    setUnresolvedDurableOperations((current) => {
      if (current[contextKey]?.prompt !== prompt) {
        return current;
      }
      const next = { ...current };
      delete next[contextKey];
      return next;
    });
  }, []);

  useEffect(() => {
    agentSubmissionGenerationRef.current += 1;
    agentSubmissionInFlightRef.current = false;
    freshConversationRef.current = false;
    errorSessionIdRef.current = null;
    agentPollControllerRef.current?.abort();
    agentPollControllerRef.current = null;
    agentTerminalHistoryRefreshRef.current = null;
    const unresolvedOperation = authorityContextKey
      ? unresolvedDurableOperations[authorityContextKey]
      : null;
    setDraft(unresolvedOperation?.prompt ?? '');
    setErrorMessage(unresolvedOperation
      ? 'Delivery could not be confirmed. Retry the unchanged message before changing conversations.'
      : null);
    setSelectedDocumentSource(null);
    setSending(false);
    setCancellingAgentRun(false);
    setAgentRun(null);
    setAgentRunAvailability('unknown');
    setAgentRunUnavailableCategory(null);
    setBoundAgentRunChatSessionId(null);
  }, [authorityContextKey]);

  useEffect(() => () => {
    agentSubmissionGenerationRef.current += 1;
    agentSubmissionInFlightRef.current = false;
    agentPollControllerRef.current?.abort();
  }, []);

  const loadConversationMessages = useCallback(
    async (activeProjectId: number, activeSessionId: number) => {
      const history = await session.client.getProjectChatMessages(activeProjectId, activeSessionId);
      const replayedMessages = history.map(mapApiMessage).filter(Boolean);
      return hydrateMessagesWithDurableAudit(
        activeProjectId,
        activeSessionId,
        history,
        replayedMessages,
        session.client
      );
    },
    [session.client]
  );

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

        const hydratedMessages = await loadConversationMessages(projectId, targetSession.id);
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
          setSessionLoadError(describeApiError(error, 'Workshop history failed to load.'));
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
    loadConversationMessages,
    session.apiStatus.status,
    session.client,
    session.tokenConfigured,
    sessionLoadRequest
  ]);

  const retrySessionLoad = useCallback(() => {
    setSessionLoadRequest((current) => current + 1);
  }, []);

  const beginAgentRunPolling = useCallback(
    (agentRunId: string, initialStatus: WorkbenchAgentRunStatus = 'Pending', activeChatSessionId = 0) => {
      if (!projectId) {
        return;
      }

      const pollingAuthorityContextKey = authorityContextKey;
      const pollingContextIsCurrent = () =>
        pollingAuthorityContextKey !== null &&
        authorityContextKeyRef.current === pollingAuthorityContextKey;
      agentPollControllerRef.current?.abort();
      const controller = new AbortController();
      agentPollControllerRef.current = controller;
      setAgentRun({
        agentRunId,
        chatSessionId: activeChatSessionId,
        status: initialStatus,
        cancellationRequested: false,
        failureCategory: null,
        retryable: false
      });
      setSending(isActiveAgentRunStatus(initialStatus));

      void (async () => {
        while (!controller.signal.aborted) {
          try {
            const snapshot = await session.client.getWorkbenchAgentRun(projectId, agentRunId, controller.signal);
            if (controller.signal.aborted || !pollingContextIsCurrent()) {
              return;
            }

            setAgentRun({
              agentRunId: snapshot.agentRunId,
              chatSessionId: snapshot.chatSessionId,
              status: snapshot.status,
              cancellationRequested: Boolean(snapshot.cancellationRequestedAtUtc),
              failureCategory: snapshot.failureCategory,
              retryable: snapshot.retryable
            });
            setBoundAgentRunChatSessionId(snapshot.chatSessionId);
            setAgentRunAvailability('available');
            setAgentRunUnavailableCategory(null);

            if (isTerminalAgentRunStatus(snapshot.status)) {
              setSending(false);
              setCancellingAgentRun(false);
              setErrorMessage(describeTerminalAgentRunOutcome(snapshot));
              agentTerminalHistoryRefreshRef.current = terminalAgentRunRefreshKey(
                pollingAuthorityContextKey,
                snapshot
              );
              setSessionLoadRequest((current) => current + 1);
              return;
            }

            setErrorMessage(null);
            await waitForAgentRunPoll(controller.signal, 750);
          } catch (error) {
            if (controller.signal.aborted || !pollingContextIsCurrent() || isAbortError(error)) {
              return;
            }
            if (error instanceof IronDevApiError && isAuthoritativeAgentRunPollingError(error)) {
              setSending(false);
              setCancellingAgentRun(false);
              setAgentRun(null);
              setAgentRunAvailability('unavailable');
              setAgentRunUnavailableCategory('status_unavailable');
              setErrorMessage(describeAuthoritativeAgentRunPollingFailure(error));
              return;
            }
            setErrorMessage('Business Analyst status is temporarily unavailable. IronDev will keep checking this run.');
            await waitForAgentRunPoll(controller.signal, 1_500);
          }
        }
      })();
    },
    [authorityContextKey, projectId, session.client]
  );

  const recoverCurrentAgentRun = useCallback(
    async (activeSessionId: number | null, signal?: AbortSignal) => {
      if (!conversationAuthorityEnabled || !projectId || !workbenchSession) {
        return null;
      }

      const recoveryAuthorityContextKey = authorityContextKey;
      setAgentRunAvailability('checking');
      const current = await session.client.getCurrentWorkbenchAgentRun(
        projectId,
        workbenchSession.workbenchSessionId,
        workbenchSession.leaseEpoch,
        null,
        signal
      );
      if (signal?.aborted ||
          recoveryAuthorityContextKey === null ||
          authorityContextKeyRef.current !== recoveryAuthorityContextKey) {
        return null;
      }
      setAgentRunAvailability(current.submissionAvailable ? 'available' : 'unavailable');
      setAgentRunUnavailableCategory(current.unavailableCategory);
      setBoundAgentRunChatSessionId(current.boundChatSessionId ?? null);

      const recoveredRun = current.activeRun ?? current.latestRun;
      if (recoveredRun &&
          (!current.boundChatSessionId || recoveredRun.chatSessionId !== current.boundChatSessionId)) {
        throw new Error('The recovered AgentRun does not match the Workbench conversation binding.');
      }
      if (current.boundChatSessionId && current.boundChatSessionId !== activeSessionId) {
        onSessionCreated(current.boundChatSessionId);
        return current;
      }
      if (current.activeRun) {
        beginAgentRunPolling(current.activeRun.agentRunId, current.activeRun.status, current.activeRun.chatSessionId);
      } else if (current.latestRun && isTerminalAgentRunStatus(current.latestRun.status)) {
        setAgentRun(toChatAgentRunState(current.latestRun));
        setSending(false);
        setCancellingAgentRun(false);
        setErrorMessage(describeTerminalAgentRunOutcome(current.latestRun));
        const refreshKey = terminalAgentRunRefreshKey(recoveryAuthorityContextKey, current.latestRun);
        if (agentTerminalHistoryRefreshRef.current !== refreshKey) {
          agentTerminalHistoryRefreshRef.current = refreshKey;
          setSessionLoadRequest((request) => request + 1);
        }
      } else {
        setAgentRun((existing) =>
          activeSessionId !== null && existing && existing.chatSessionId === activeSessionId && isTerminalAgentRunStatus(existing.status)
            ? existing
            : null
        );
        setSending(false);
      }
      return current;
    },
    [authorityContextKey, beginAgentRunPolling, conversationAuthorityEnabled, onSessionCreated, projectId, session.client, workbenchSession]
  );

  useEffect(() => {
    agentPollControllerRef.current?.abort();
    agentPollControllerRef.current = null;

    if (!conversationAuthorityEnabled) {
      setAgentRun(null);
      setAgentRunAvailability('unknown');
      setAgentRunUnavailableCategory(null);
      setBoundAgentRunChatSessionId(null);
      return;
    }

    if (agentSubmissionInFlightRef.current) {
      return;
    }

    if (!projectId || !workbenchSession) {
      setAgentRun(null);
      setAgentRunAvailability('unknown');
      setAgentRunUnavailableCategory(null);
      setBoundAgentRunChatSessionId(null);
      setSending(false);
      return;
    }

    if (sessionLoadState !== 'ready') {
      return;
    }

    const controller = new AbortController();
    const recoveryAuthorityContextKey = authorityContextKey;
    void recoverCurrentAgentRun(sessionId, controller.signal).catch((error) => {
      if (controller.signal.aborted ||
          recoveryAuthorityContextKey === null ||
          authorityContextKeyRef.current !== recoveryAuthorityContextKey ||
          isAbortError(error)) {
        return;
      }
      setAgentRunAvailability('unavailable');
      setAgentRunUnavailableCategory('status_unavailable');
      setErrorMessage('Business Analyst status could not be verified. Refresh Workshop before sending another message.');
    });

    return () => {
      controller.abort();
      agentPollControllerRef.current?.abort();
      agentPollControllerRef.current = null;
    };
  }, [
    conversationAuthorityEnabled,
    projectId,
    recoverCurrentAgentRun,
    sessionId,
    sessionLoadState,
    workbenchSession
  ]);

  const loadDocumentSources = useCallback(async () => {
    if (!projectId || !session.tokenConfigured || session.apiStatus.status !== 'connected') {
      setDocumentSources([]);
      setDocumentSourceLoadState('error');
      setDocumentSourceError('Project document sources are unavailable until Workshop is connected.');
      return;
    }

    setDocumentSourceLoadState('loading');
    setDocumentSourceError(null);
    try {
      const sources = await session.client.getProjectChatDocumentSources(projectId);
      setDocumentSources(sources);
      setDocumentSourceLoadState('ready');
    } catch (error) {
      setDocumentSources([]);
      setDocumentSourceLoadState('error');
      setDocumentSourceError(describeApiError(error, 'Project document sources failed to load.'));
    }
  }, [projectId, session.apiStatus.status, session.client, session.tokenConfigured]);

  const startNewConversation = useCallback(() => {
    if (conversationAuthorityEnabled && (hasUnresolvedDurableOperation || isSending || boundAgentRunChatSessionId !== null)) {
      setErrorMessage(hasUnresolvedDurableOperation
        ? 'Delivery could not be confirmed. Retry the unchanged message before changing conversations.'
        : isSending
          ? 'Wait for the governed Business Analyst turn to settle before changing conversations.'
          : 'This Workbench session is permanently bound to its governed conversation. Starting another direct conversation is not available in this preview.');
      return false;
    }

    freshConversationRef.current = true;
    agentPollControllerRef.current?.abort();
    setSessionId(null);
    setMessages([]);
    setDraft('');
    setSelectedDocumentSource(null);
    errorSessionIdRef.current = null;
    setErrorMessage(null);
    setSessionLoadState('ready');
    return true;
  }, [boundAgentRunChatSessionId, conversationAuthorityEnabled, hasUnresolvedDurableOperation, isSending]);

  const ensureChatSession = useCallback(
    async (prompt: string, expectedAuthorityContextKey: string | null = null) => {
      if (!projectId || !workbenchSession) {
        throw new Error('An active Workbench session is required before creating a chat session.');
      }

      const unresolvedSessionCreate = expectedAuthorityContextKey
        ? unresolvedDurableOperations[expectedAuthorityContextKey]?.kind === 'CreateSession'
        : false;
      if (sessionId && !unresolvedSessionCreate) {
        return { id: sessionId, created: false };
      }

      const title = createSessionTitle(prompt);
      const operationKey = `${projectId}:${workbenchSession.workbenchSessionId}:${title}`;
      const operationAttempt = chatSessionCreationAttemptsRef.current.get(operationKey) ?? {
        key: operationKey,
        clientOperationId: crypto.randomUUID()
      };
      chatSessionCreationAttemptsRef.current.set(operationKey, operationAttempt);

      let createdSessionId: number;
      try {
        createdSessionId = await session.client.saveProjectChatSession(projectId, {
          projectId,
          title,
          summary: 'Project conversation',
          workbenchSessionId: workbenchSession.workbenchSessionId,
          leaseEpoch: workbenchSession.leaseEpoch,
          clientOperationId: operationAttempt.clientOperationId
        });
        if (chatSessionCreationAttemptsRef.current.get(operationKey) === operationAttempt) {
          chatSessionCreationAttemptsRef.current.delete(operationKey);
        }
        if (expectedAuthorityContextKey) {
          clearDurableOperationUnresolved(expectedAuthorityContextKey, prompt);
        }
        if (expectedAuthorityContextKey && authorityContextKeyRef.current !== expectedAuthorityContextKey) {
          throw new StaleWorkbenchSubmissionContextError();
        }
      } catch (error) {
        if (error instanceof IronDevApiError &&
            chatSessionCreationAttemptsRef.current.get(operationKey) === operationAttempt) {
          chatSessionCreationAttemptsRef.current.delete(operationKey);
        }
        if (expectedAuthorityContextKey && error instanceof IronDevApiError) {
          clearDurableOperationUnresolved(expectedAuthorityContextKey, prompt);
        } else if (expectedAuthorityContextKey &&
                   !(error instanceof StaleWorkbenchSubmissionContextError)) {
          markDurableOperationUnresolved(expectedAuthorityContextKey, prompt, 'CreateSession');
        }
        throw error;
      }
      freshConversationRef.current = false;
      setSessionId(createdSessionId);
      return { id: createdSessionId, created: true };
    },
    [
      clearDurableOperationUnresolved,
      markDurableOperationUnresolved,
      projectId,
      session.client,
      sessionId,
      unresolvedDurableOperations,
      workbenchSession
    ]
  );

  const sendMessage = useCallback(
    async (request?: ChatSendRequest) => {
      const prompt = (request?.prompt ?? draft).trim();
      const displayText = request?.displayText ?? prompt;
      const unresolvedRetryBlocked = unresolvedDurableOperation &&
        prompt !== unresolvedDurableOperation.prompt;
      const blockedReason = disabledReason ??
        agentRunReadinessReason ??
        (unresolvedRetryBlocked
          ? 'Delivery is unresolved. Retry the unchanged message before starting another operation.'
          : null) ??
        (request ? (isSending ? 'Workshop request is already sending.' : null) : getChatSendBlockedReason(draft, isSending));

      if (!projectId || !workbenchSession || blockedReason || !prompt) {
        return;
      }

      const attachedSource = conversationAuthorityEnabled ? null : (request ? null : selectedDocumentSource);
      const submissionContextKey = conversationAuthorityEnabled ? authorityContextKey : null;
      const submissionGeneration = agentSubmissionGenerationRef.current + 1;
      agentSubmissionGenerationRef.current = submissionGeneration;
      const submissionIsCurrent = () =>
        !conversationAuthorityEnabled ||
        (submissionContextKey !== null &&
          authorityContextKeyRef.current === submissionContextKey &&
          agentSubmissionGenerationRef.current === submissionGeneration);

      const userMessage: ChatWorkspaceMessage = {
        id: `user-${Date.now()}`,
        role: 'user',
        content: conversationAuthorityEnabled ? prompt : displayText,
        canContinueInBuild: request?.canContinueInBuild ?? true,
        createdUtc: new Date().toISOString(),
        documentSources: attachedSource ? [attachedSource] : [],
        deliveryState: conversationAuthorityEnabled ? 'submitting' : undefined
      };

      setMessages((current) => [...current, userMessage]);
      setSending(true);
      agentSubmissionInFlightRef.current = conversationAuthorityEnabled;
      errorSessionIdRef.current = null;
      setErrorMessage(null);
      let createdSessionId: number | null = null;
      let governedSessionId: number | null = null;
      let preserveAgentRunSendingState = false;

      try {
        const activeSession = await ensureChatSession(prompt, submissionContextKey);
        if (!submissionIsCurrent()) {
          return;
        }
        const activeSessionId = activeSession.id;
        createdSessionId = activeSession.created ? activeSessionId : null;

        if (conversationAuthorityEnabled) {
          governedSessionId = activeSessionId;
          const submissionKey = `${projectId}:${workbenchSession.workbenchSessionId}:${workbenchSession.leaseEpoch}:${activeSessionId}:${prompt}`;
          const operationAttempt = agentSubmissionAttemptsRef.current.get(submissionKey) ?? {
            key: submissionKey,
            clientOperationId: crypto.randomUUID()
          };
          agentSubmissionAttemptsRef.current.set(submissionKey, operationAttempt);

          try {
            const submitted = await session.client.submitWorkbenchAgentRun(projectId, {
              workbenchSessionId: workbenchSession.workbenchSessionId,
              leaseEpoch: workbenchSession.leaseEpoch,
              clientOperationId: operationAttempt.clientOperationId,
              chatSessionId: activeSessionId,
              message: prompt
            });
            if (agentSubmissionAttemptsRef.current.get(submissionKey) === operationAttempt) {
              agentSubmissionAttemptsRef.current.delete(submissionKey);
            }
            if (submissionContextKey) {
              clearDurableOperationUnresolved(submissionContextKey, prompt);
            }
            if (!submissionIsCurrent()) {
              return;
            }
            preserveAgentRunSendingState = true;
            setAgentRunAvailability('available');
            setAgentRunUnavailableCategory(null);
            setBoundAgentRunChatSessionId(activeSessionId);
            setMessages((current) =>
              current.map((message) =>
                message.id === userMessage.id
                  ? { ...message, id: `user-${submitted.userMessageId}`, deliveryState: 'accepted' }
                  : message
              )
            );
            if (!request) {
              setDraft('');
            }
            setSelectedDocumentSource(null);
            beginAgentRunPolling(submitted.agentRunId, submitted.status, activeSessionId);
          } catch (error) {
            if (error instanceof IronDevApiError) {
              if (agentSubmissionAttemptsRef.current.get(submissionKey) === operationAttempt) {
                agentSubmissionAttemptsRef.current.delete(submissionKey);
              }
              if (submissionContextKey) {
                clearDurableOperationUnresolved(submissionContextKey, prompt);
              }
            } else if (submissionContextKey) {
              markDurableOperationUnresolved(submissionContextKey, prompt, 'SubmitRun');
              setSessionLoadRequest((current) => current + 1);
            }
            if (!submissionIsCurrent()) {
              return;
            }
            if (!(error instanceof IronDevApiError)) {
              setMessages((current) =>
                current.map((message) =>
                  message.id === userMessage.id ? { ...message, deliveryState: 'uncertain' } : message
                )
              );
              setErrorMessage('Delivery could not be confirmed. Send the unchanged message again to retry the same operation safely.');
              return;
            }

            const errorBody = readAgentRunErrorBody(error.body);
            setMessages((current) => current.filter((message) => message.id !== userMessage.id));

            if (error.status === 409 && errorBody.error === 'workbench_agent_run_active') {
              setErrorMessage('Another Business Analyst turn is already active. IronDev is tracking that run instead.');
              const activeRunId = errorBody.agentRunId;
              if (activeRunId) {
                preserveAgentRunSendingState = true;
                setBoundAgentRunChatSessionId(activeSessionId);
                beginAgentRunPolling(activeRunId, 'Pending', activeSessionId);
              } else {
                const recovered = await recoverCurrentAgentRun(activeSessionId);
                preserveAgentRunSendingState = Boolean(recovered?.activeRun);
              }
              return;
            }

            if (error.status === 503 && errorBody.error === 'workbench_agent_run_unavailable') {
              setAgentRunAvailability('unavailable');
              setAgentRunUnavailableCategory(errorBody.failureCategory ?? 'service_unavailable');
            }
            setErrorMessage(describeAgentRunSubmissionError(error, errorBody));
          }
          return;
        }

        const savedUserMessageId = await session.client.saveProjectChatMessage(projectId, activeSessionId, {
          projectId,
          chatSessionId: activeSessionId,
          role: 'user',
          message: displayText,
          tags: request?.mode ?? 'projectQuestion',
          documentVersionIds: attachedSource ? [attachedSource.documentVersionId] : [],
          workbenchSessionId: workbenchSession.workbenchSessionId,
          leaseEpoch: workbenchSession.leaseEpoch,
          clientOperationId: crypto.randomUUID()
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
          mode: request?.mode ?? 'projectQuestion',
          sourceMessageId: savedUserMessageId,
          workbenchSessionId: workbenchSession.workbenchSessionId,
          leaseEpoch: workbenchSession.leaseEpoch,
          clientOperationId: crypto.randomUUID()
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
          linkedSymbols: response.linkedSymbols ?? null,
          replyToMessageId: savedUserMessageId,
          workbenchSessionId: workbenchSession.workbenchSessionId,
          leaseEpoch: workbenchSession.leaseEpoch,
          clientOperationId: crypto.randomUUID()
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
            traceId: response.traceId ?? null,
            documentSources: response.documentSources ?? (attachedSource ? [attachedSource] : [])
          },
          createdUtc: new Date().toISOString(),
          documentSources: response.documentSources ?? (attachedSource ? [attachedSource] : [])
        };

        setMessages((current) => [...current, assistantMessage]);
        if (!request) {
          setDraft('');
          setSelectedDocumentSource(null);
        }
      } catch (error) {
        if (!submissionIsCurrent() || error instanceof StaleWorkbenchSubmissionContextError) {
          return;
        }
        errorSessionIdRef.current = createdSessionId ?? sessionId;
        if (conversationAuthorityEnabled) {
          if (error instanceof IronDevApiError) {
            setMessages((current) => current.filter((message) => message.id !== userMessage.id));
          } else {
            setMessages((current) =>
              current.map((message) =>
                message.id === userMessage.id ? { ...message, deliveryState: 'uncertain' } : message
              )
            );
          }
          if (!(error instanceof IronDevApiError)) {
            setSessionLoadRequest((current) => current + 1);
          }
        }
        setErrorMessage(
          conversationAuthorityEnabled && !(error instanceof IronDevApiError)
            ? 'Delivery could not be confirmed while opening the conversation. Send the unchanged message again to retry safely.'
            : describeApiError(error, 'Send failed.')
        );
      } finally {
        if (agentSubmissionGenerationRef.current === submissionGeneration) {
          agentSubmissionInFlightRef.current = false;
          if (!preserveAgentRunSendingState) {
            setSending(false);
          }
          if (submissionIsCurrent()) {
            if (governedSessionId) {
              onSessionCreated(governedSessionId);
            } else if (createdSessionId) {
              onSessionCreated(createdSessionId);
            }
          }
        }
      }
    },
    [
      agentRunReadinessReason,
      authorityContextKey,
      beginAgentRunPolling,
      clearDurableOperationUnresolved,
      conversationAuthorityEnabled,
      disabledReason,
      draft,
      ensureChatSession,
      isSending,
      markDurableOperationUnresolved,
      onSessionCreated,
      projectId,
      recoverCurrentAgentRun,
      selectedDocumentSource,
      session.client,
      sessionId,
      unresolvedDurableOperation,
      workbenchSession
    ]
  );

  const reviewProjectState = useCallback(() => {
    void sendMessage({
      prompt: projectReviewPrompt,
      displayText: 'Review Project State',
      mode: 'projectStateReview',
      canContinueInBuild: false
    });
  }, [sendMessage]);

  const cancelAgentRun = useCallback(async () => {
    if (!conversationAuthorityEnabled || !projectId || !workbenchSession || !agentRun || !agentRunIsActive || isCancellingAgentRun) {
      return;
    }

    const cancellationKey = `${projectId}:${agentRun.agentRunId}:${workbenchSession.workbenchSessionId}:${workbenchSession.leaseEpoch}`;
    const operationAttempt = agentCancellationAttemptsRef.current.get(cancellationKey) ?? {
      key: cancellationKey,
      clientOperationId: crypto.randomUUID()
    };
    const cancellationAuthorityContextKey = authorityContextKey;
    const cancellationIsCurrent = () =>
      cancellationAuthorityContextKey !== null &&
      authorityContextKeyRef.current === cancellationAuthorityContextKey &&
      agentCancellationAttemptsRef.current.get(cancellationKey) === operationAttempt;
    agentCancellationAttemptsRef.current.set(cancellationKey, operationAttempt);
    setCancellingAgentRun(true);
    setErrorMessage(null);
    try {
      const cancelled = await session.client.cancelWorkbenchAgentRun(projectId, agentRun.agentRunId, {
        workbenchSessionId: workbenchSession.workbenchSessionId,
        leaseEpoch: workbenchSession.leaseEpoch,
        clientOperationId: operationAttempt.clientOperationId
      });
      if (!cancellationIsCurrent()) {
        return;
      }
      agentCancellationAttemptsRef.current.delete(cancellationKey);
      setAgentRun((current) => current?.agentRunId === cancelled.agentRunId
        ? {
            ...current,
            status: cancelled.status,
            cancellationRequested: cancelled.cancellationRequested
          }
        : current);
      if (isTerminalAgentRunStatus(cancelled.status)) {
        setSending(false);
        setSessionLoadRequest((current) => current + 1);
      }
    } catch (error) {
      if (!cancellationIsCurrent()) {
        return;
      }
      if (error instanceof IronDevApiError) {
        agentCancellationAttemptsRef.current.delete(cancellationKey);
      }
      setErrorMessage(
        error instanceof IronDevApiError
          ? describeApiError(error, 'The Business Analyst run could not be cancelled.')
          : 'Cancellation delivery could not be confirmed. Try Cancel again to replay the same operation safely.'
      );
    } finally {
      if (cancellationIsCurrent() || !agentCancellationAttemptsRef.current.has(cancellationKey)) {
        setCancellingAgentRun(false);
      }
    }
  }, [
    agentRun,
    agentRunIsActive,
    authorityContextKey,
    conversationAuthorityEnabled,
    isCancellingAgentRun,
    projectId,
    session.client,
    workbenchSession
  ]);

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
        throw new Error('Create ticket failed. A project Workshop session is required.');
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
    isCancellingAgentRun,
    agentRun,
    agentRunIsActive,
    agentRunAvailability,
    conversationAuthorityEnabled,
    hasUnresolvedDurableOperation,
    boundAgentRunChatSessionId,
    disabledReason,
    sendDisabledReason,
    errorMessage,
    latestResponse: latestResponse as ChatCompletionResponse | null,
    latestResponseText,
    documentSources,
    documentSourceLoadState,
    documentSourceError,
    selectedDocumentSource,
    projectLabel,
    retrySessionLoad,
    startNewConversation,
    setDraft,
    loadDocumentSources,
    setSelectedDocumentSource,
    sendMessage,
    cancelAgentRun,
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
          auditHasFallbackEvidence: false,
          documentSources: message.documentSources ?? []
        }
      : null,
    documentSources: message.documentSources ?? []
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

function getChatBlockedReason(tokenConfigured: boolean, projectId: number | null, hasWorkbenchSession: boolean, apiStatus: string, accessStatus: string) {
  if (!tokenConfigured) {
    return 'Authentication is required before chat can use project context.';
  }

  if (!projectId) {
    return 'Select a project before chatting with IronDev.';
  }

  if (!hasWorkbenchSession) {
    return 'Open the project in Workbench before sending a message.';
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
    return 'Workshop request is already sending.';
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

interface AgentRunErrorBody {
  error: string | null;
  message: string | null;
  failureCategory: string | null;
  retryable: boolean | null;
  agentRunId: string | null;
}

function readAgentRunErrorBody(body: unknown): AgentRunErrorBody {
  if (!body || typeof body !== 'object') {
    return { error: null, message: null, failureCategory: null, retryable: null, agentRunId: null };
  }

  const value = body as Record<string, unknown>;
  return {
    error: typeof value.error === 'string' ? value.error : null,
    message: typeof value.message === 'string' ? value.message : null,
    failureCategory: typeof value.failureCategory === 'string' ? value.failureCategory : null,
    retryable: typeof value.retryable === 'boolean' ? value.retryable : null,
    agentRunId: typeof value.agentRunId === 'string' ? value.agentRunId : null
  };
}

function describeAgentRunSubmissionError(error: IronDevApiError, body: AgentRunErrorBody) {
  if (error.status === 503 || body.error === 'workbench_agent_run_unavailable') {
    return describeAgentRunUnavailable(body.failureCategory);
  }

  if (body.error === 'workbench_chat_session_mismatch') {
    return 'This Workbench session is bound to a different governed conversation. Starting another direct conversation is not available in this preview.';
  }

  if (body.error === 'workbench_lease_fence_rejected') {
    return 'The Workbench write lease changed. Reopen the project before sending another message.';
  }

  if (error.status === 404) {
    return 'The project, Workbench session, or conversation is no longer accessible.';
  }

  if (error.status === 400) {
    return 'The Business Analyst message was rejected before any run was created.';
  }

  return `The Business Analyst message was not accepted (HTTP ${error.status}).`;
}

function describeAuthoritativeAgentRunPollingFailure(error: IronDevApiError) {
  const body = readAgentRunErrorBody(error.body);
  if (body.error === 'workbench_lease_fence_rejected') {
    return 'The Workbench write lease changed. Reopen the project before continuing this run.';
  }
  if (body.error === 'workbench_chat_session_mismatch') {
    return 'This Workbench session is bound to another governed conversation. Refresh Workshop to restore it.';
  }
  if (error.status === 401 || error.status === 403 || error.status === 404) {
    return 'This Business Analyst run is no longer accessible. Reopen the project after restoring access.';
  }
  return `Business Analyst status was authoritatively rejected (HTTP ${error.status}). Refresh Workshop before continuing.`;
}

function isAuthoritativeAgentRunPollingError(error: IronDevApiError) {
  return error.status === 400 ||
    error.status === 401 ||
    error.status === 403 ||
    error.status === 404 ||
    error.status === 409;
}

function describeAgentRunUnavailable(category: string | null) {
  if (category === 'status_unavailable') {
    return 'Business Analyst readiness could not be verified. Refresh Workshop before sending.';
  }

  return 'The governed Business Analyst service is unavailable in this environment. You can continue reviewing existing Workshop history.';
}

function describeTerminalAgentRunFailure(category: string | null, retryable: boolean) {
  const categoryText = category ? ` (${category.replace(/_/g, ' ')})` : '';
  return retryable
    ? `The Business Analyst run failed safely${categoryText}. Follow the server-provided retry guidance before trying again.`
    : `The Business Analyst run failed safely${categoryText}. This run cannot be retried; send a new message when the service is ready.`;
}

function describeTerminalAgentRunOutcome(snapshot: WorkbenchAgentRunSnapshot) {
  if (snapshot.status === 'Failed') {
    return describeTerminalAgentRunFailure(snapshot.failureCategory, snapshot.retryable);
  }
  if (snapshot.status === 'Superseded' || snapshot.status === 'Stale') {
    return 'The Business Analyst run no longer owns the current Workbench session. Refresh before sending another message.';
  }
  return null;
}

function terminalAgentRunRefreshKey(
  authorityContextKey: string | null,
  snapshot: WorkbenchAgentRunSnapshot
) {
  return `${authorityContextKey ?? 'no-authority'}:${snapshot.agentRunId}:${snapshot.status}:${snapshot.assistantMessageId ?? 'no-assistant'}`;
}

function isActiveAgentRunStatus(status: WorkbenchAgentRunStatus | null | undefined) {
  return status === 'Pending' || status === 'Running';
}

function toChatAgentRunState(snapshot: WorkbenchAgentRunSnapshot): ChatAgentRunState {
  return {
    agentRunId: snapshot.agentRunId,
    chatSessionId: snapshot.chatSessionId,
    status: snapshot.status,
    cancellationRequested: Boolean(snapshot.cancellationRequestedAtUtc),
    failureCategory: snapshot.failureCategory,
    retryable: snapshot.retryable
  };
}

function isTerminalAgentRunStatus(status: WorkbenchAgentRunStatus) {
  return !isActiveAgentRunStatus(status);
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError';
}

function waitForAgentRunPoll(signal: AbortSignal, delayMs: number) {
  return new Promise<void>((resolve) => {
    if (signal.aborted) {
      resolve();
      return;
    }

    const timeoutId = window.setTimeout(() => {
      signal.removeEventListener('abort', onAbort);
      resolve();
    }, delayMs);
    const onAbort = () => {
      window.clearTimeout(timeoutId);
      resolve();
    };
    signal.addEventListener('abort', onAbort, { once: true });
  });
}
