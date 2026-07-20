import { useCallback, useEffect, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ChatMessage,
  TicketProposalCommitResult,
  TicketProposalReadModel,
  TicketProposalRevisionReadModel,
  TicketProposalSetReadModel
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

type TicketProposalLoadState = 'idle' | 'loading' | 'ready' | 'error';

interface RetainedProposalOperation {
  key: string;
  clientOperationId: string;
}

export interface TicketProposalController {
  proposalSet: TicketProposalSetReadModel | null;
  history: TicketProposalRevisionReadModel[];
  loadState: TicketProposalLoadState;
  error: string | null;
  mutationError: string | null;
  isMutating: boolean;
  isRegenerating: boolean;
  isCommitting: boolean;
  deliveryUnresolved: boolean;
  commitDeliveryUnresolved: boolean;
  commitResult: TicketProposalCommitResult | null;
  refresh: () => void;
  loadSourceMessage: (messageId: number) => Promise<ChatMessage | null>;
  updateProposal: (proposal: TicketProposalReadModel) => Promise<boolean>;
  moveProposal: (proposalId: string, direction: -1 | 1) => Promise<boolean>;
  removeProposal: (proposalId: string) => Promise<boolean>;
  resolveIssue: (issueId: string, resolution: string) => Promise<boolean>;
  regenerate: (instruction: string, chatSessionId: number | null) => Promise<boolean>;
  commitTickets: () => Promise<boolean>;
}

export function useTicketProposals({
  enabled,
  refreshKey,
  onRegenerationTerminal
}: {
  enabled: boolean;
  refreshKey: string | null;
  onRegenerationTerminal?: () => void;
}): TicketProposalController {
  const session = useSessionContext();
  const project = useProjectContext();
  const [proposalSet, setProposalSet] = useState<TicketProposalSetReadModel | null>(null);
  const [history, setHistory] = useState<TicketProposalRevisionReadModel[]>([]);
  const [loadState, setLoadState] = useState<TicketProposalLoadState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [isMutating, setMutating] = useState(false);
  const [isRegenerating, setRegenerating] = useState(false);
  const [isCommitting, setCommitting] = useState(false);
  const [deliveryUnresolved, setDeliveryUnresolved] = useState(false);
  const [commitDeliveryUnresolved, setCommitDeliveryUnresolved] = useState(false);
  const [commitResult, setCommitResult] = useState<TicketProposalCommitResult | null>(null);
  const [loadRequest, setLoadRequest] = useState(0);
  const operationAttemptsRef = useRef(new Map<string, RetainedProposalOperation>());
  const authorityGenerationRef = useRef(0);
  const projectId = project.selectedProjectId;
  const workbenchSession = project.workbenchSession;
  const authorityKey = projectId && workbenchSession
    ? `${projectId}:${workbenchSession.workbenchSessionId}:${workbenchSession.leaseEpoch}`
    : null;

  useEffect(() => {
    authorityGenerationRef.current += 1;
    setProposalSet(null);
    setHistory([]);
    setLoadState(enabled && authorityKey ? 'loading' : 'idle');
    setError(null);
    setMutationError(null);
    setMutating(false);
    setRegenerating(false);
    setCommitting(false);
    setDeliveryUnresolved(false);
    setCommitDeliveryUnresolved(false);
    setCommitResult(null);
  }, [authorityKey, enabled]);

  useEffect(() => {
    if (!enabled || !projectId || !workbenchSession || !authorityKey || deliveryUnresolved) {
      return;
    }
    let cancelled = false;
    const generation = authorityGenerationRef.current;
    const load = async () => {
      setLoadState('loading');
      setError(null);
      try {
        const current = await session.client.getCurrentTicketProposalSet(
          projectId,
          workbenchSession.workbenchSessionId,
          workbenchSession.leaseEpoch
        );
        if (cancelled || generation !== authorityGenerationRef.current) {
          return;
        }
        const nextHistory = current
          ? await session.client.getTicketProposalSetHistory(
              projectId,
              current.ticketProposalSetId,
              workbenchSession.workbenchSessionId,
              workbenchSession.leaseEpoch
            )
          : [];
        if (cancelled || generation !== authorityGenerationRef.current) {
          return;
        }
        setProposalSet(current);
        setHistory(nextHistory);
        setLoadState('ready');
      } catch (loadError) {
        if (!cancelled && generation === authorityGenerationRef.current) {
          setLoadState('error');
          setError(describeProposalError(loadError, 'Ticket proposals could not be loaded.'));
        }
      }
    };
    void load();
    return () => { cancelled = true; };
  }, [authorityKey, deliveryUnresolved, enabled, loadRequest, projectId, refreshKey, session.client, workbenchSession]);

  const applyMutation = useCallback(async (
    action: string,
    payloadKey: string,
    invoke: (clientOperationId: string) => Promise<{ proposalSet: TicketProposalSetReadModel }>
  ) => {
    if (!proposalSet || isMutating || !authorityKey) {
      return false;
    }
    const operationKey = `${authorityKey}:${proposalSet.ticketProposalSetId}:${proposalSet.revision}:${action}:${payloadKey}`;
    const retainedAttempt = operationAttemptsRef.current.get(operationKey);
    if (deliveryUnresolved && !retainedAttempt) {
      setMutationError('A previous delivery is unresolved. Retry that exact unchanged review action before starting another change.');
      return false;
    }
    const attempt = retainedAttempt ?? {
      key: operationKey,
      clientOperationId: crypto.randomUUID()
    };
    operationAttemptsRef.current.set(operationKey, attempt);
    const generation = authorityGenerationRef.current;
    setMutating(true);
    setMutationError(null);
    try {
      const result = await invoke(attempt.clientOperationId);
      operationAttemptsRef.current.delete(operationKey);
      setDeliveryUnresolved(false);
      setCommitDeliveryUnresolved(false);
      if (generation !== authorityGenerationRef.current) {
        return true;
      }
      setProposalSet(result.proposalSet);
      if (projectId && workbenchSession) {
        try {
          const nextHistory = await session.client.getTicketProposalSetHistory(
            projectId,
            result.proposalSet.ticketProposalSetId,
            workbenchSession.workbenchSessionId,
            workbenchSession.leaseEpoch
          );
          if (generation === authorityGenerationRef.current) {
            setHistory(nextHistory);
          }
        } catch {
          if (generation === authorityGenerationRef.current) {
            setMutationError('The proposal change was saved, but revision history could not be refreshed.');
          }
        }
      }
      return true;
    } catch (mutationFailure) {
      const definitive = isDefinitiveProposalFailure(mutationFailure);
      if (definitive) {
        operationAttemptsRef.current.delete(operationKey);
      }
      if (isProposalRevisionConflict(mutationFailure)) {
        setDeliveryUnresolved(false);
        setMutationError('The proposal set changed elsewhere. Reloading the latest durable revision.');
        setLoadRequest((current) => current + 1);
        return false;
      }
      setDeliveryUnresolved(!definitive);
      setMutationError(definitive
        ? describeProposalError(mutationFailure, 'The ticket proposal change was rejected.')
        : 'Delivery could not be confirmed. Retry the unchanged action to replay its exact operation safely.');
      return false;
    } finally {
      if (generation === authorityGenerationRef.current) {
        setMutating(false);
      }
    }
  }, [authorityKey, deliveryUnresolved, isMutating, projectId, proposalSet, session.client, workbenchSession]);

  const mutationAuthority = useCallback((clientOperationId: string) => {
    if (!workbenchSession || !proposalSet) {
      throw new Error('A current proposal set and Workbench lease are required.');
    }
    return {
      workbenchSessionId: workbenchSession.workbenchSessionId,
      leaseEpoch: workbenchSession.leaseEpoch,
      expectedProposalSetRevision: proposalSet.revision,
      clientOperationId
    };
  }, [proposalSet, workbenchSession]);

  const updateProposal = useCallback(async (proposal: TicketProposalReadModel) => {
    if (!projectId || !proposalSet) {
      return false;
    }
    const payload = JSON.stringify({
      ticketProposalId: proposal.ticketProposalId,
      title: proposal.title,
      problem: proposal.problem,
      proposedChange: proposal.proposedChange,
      acceptanceCriteria: proposal.acceptanceCriteria
    });
    return applyMutation('edit', payload, (clientOperationId) => session.client.updateTicketProposal(
      projectId,
      proposalSet.ticketProposalSetId,
      proposal.ticketProposalId,
      {
        ...mutationAuthority(clientOperationId),
        title: proposal.title,
        problem: proposal.problem,
        proposedChange: proposal.proposedChange,
        acceptanceCriteria: proposal.acceptanceCriteria
      }
    ));
  }, [applyMutation, mutationAuthority, projectId, proposalSet, session.client]);

  const moveProposal = useCallback(async (proposalId: string, direction: -1 | 1) => {
    if (!projectId || !proposalSet) {
      return false;
    }
    const ordered = [...proposalSet.proposals]
      .sort((left, right) => left.suggestedOrder - right.suggestedOrder)
      .map((proposal) => proposal.ticketProposalId);
    const index = ordered.indexOf(proposalId);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= ordered.length) {
      return false;
    }
    [ordered[index], ordered[target]] = [ordered[target], ordered[index]];
    return applyMutation('reorder', JSON.stringify(ordered), (clientOperationId) =>
      session.client.reorderTicketProposals(projectId, proposalSet.ticketProposalSetId, {
        ...mutationAuthority(clientOperationId),
        orderedProposalIds: ordered
      }));
  }, [applyMutation, mutationAuthority, projectId, proposalSet, session.client]);

  const removeProposal = useCallback(async (proposalId: string) => {
    if (!projectId || !proposalSet) {
      return false;
    }
    return applyMutation('remove', proposalId, (clientOperationId) =>
      session.client.removeTicketProposal(
        projectId,
        proposalSet.ticketProposalSetId,
        proposalId,
        mutationAuthority(clientOperationId)
      ));
  }, [applyMutation, mutationAuthority, projectId, proposalSet, session.client]);

  const resolveIssue = useCallback(async (issueId: string, resolution: string) => {
    if (!projectId || !proposalSet || !resolution.trim()) {
      return false;
    }
    return applyMutation('resolve', JSON.stringify({ issueId, resolution: resolution.trim() }), (clientOperationId) =>
      session.client.resolveTicketProposalIssue(
        projectId,
        proposalSet.ticketProposalSetId,
        issueId,
        { ...mutationAuthority(clientOperationId), resolution: resolution.trim() }
      ));
  }, [applyMutation, mutationAuthority, projectId, proposalSet, session.client]);

  const regenerate = useCallback(async (instruction: string, chatSessionId: number | null) => {
    if (!projectId || !proposalSet || !workbenchSession || !authorityKey || !chatSessionId || isRegenerating) {
      return false;
    }
    const normalizedInstruction = instruction.trim();
    if (!normalizedInstruction) {
      setMutationError('Add an instruction describing what should change in the proposal set.');
      return false;
    }
    const operationKey = `${authorityKey}:${proposalSet.ticketProposalSetId}:${proposalSet.revision}:regenerate:${normalizedInstruction}`;
    const retainedAttempt = operationAttemptsRef.current.get(operationKey);
    if (deliveryUnresolved && !retainedAttempt) {
      setMutationError('A previous delivery is unresolved. Retry that exact unchanged review action before regenerating.');
      return false;
    }
    const attempt = retainedAttempt ?? {
      key: operationKey,
      clientOperationId: crypto.randomUUID()
    };
    operationAttemptsRef.current.set(operationKey, attempt);
    const generation = authorityGenerationRef.current;
    setRegenerating(true);
    setMutationError(null);
    try {
      let run;
      try {
        run = await session.client.regenerateTicketProposals(projectId, proposalSet.ticketProposalSetId, {
          ...mutationAuthority(attempt.clientOperationId),
          instruction: normalizedInstruction,
          chatSessionId
        });
      } catch (regenerationFailure) {
        const definitive = isDefinitiveProposalFailure(regenerationFailure);
        if (definitive) {
          operationAttemptsRef.current.delete(operationKey);
        }
        setDeliveryUnresolved(!definitive);
        setMutationError(definitive
          ? describeProposalError(regenerationFailure, 'Regeneration was rejected.')
          : 'Regeneration delivery could not be confirmed. Retry the unchanged instruction safely.');
        return false;
      }
      operationAttemptsRef.current.delete(operationKey);
      setDeliveryUnresolved(false);
      let status = run.status;
      try {
        while (status === 'Pending' || status === 'Running') {
          await delay(500);
          if (generation !== authorityGenerationRef.current) {
            return true;
          }
          const snapshot = await session.client.getWorkbenchAgentRun(projectId, run.agentRunId);
          status = snapshot.status;
        }
      } catch {
        if (generation === authorityGenerationRef.current) {
          onRegenerationTerminal?.();
          setLoadRequest((current) => current + 1);
          setMutationError('Regeneration was accepted, but its completion could not be refreshed. Reload the proposal review to recover the run result.');
        }
        return false;
      }
      if (generation !== authorityGenerationRef.current) {
        return true;
      }
      onRegenerationTerminal?.();
      setLoadRequest((current) => current + 1);
      const completed = status === 'Completed' || status === 'NeedsInput';
      if (!completed) {
        setMutationError(`Regeneration ended in ${status}. The previous proposal revision remains available.`);
      }
      return completed;
    } finally {
      if (generation === authorityGenerationRef.current) {
        setRegenerating(false);
      }
    }
  }, [authorityKey, deliveryUnresolved, isRegenerating, mutationAuthority, onRegenerationTerminal, projectId, proposalSet, session.client, workbenchSession]);

  const commitTickets = useCallback(async () => {
    if (!projectId || !proposalSet || !workbenchSession || !authorityKey || isCommitting ||
        (proposalSet.status !== 'Ready' && proposalSet.status !== 'Committed')) {
      return false;
    }
    if (proposalSet.status === 'Committed') {
      return commitResult !== null;
    }
    const operationKey = `${authorityKey}:${proposalSet.ticketProposalSetId}:${proposalSet.revision}:commit`;
    const retainedAttempt = operationAttemptsRef.current.get(operationKey);
    if (deliveryUnresolved && !retainedAttempt) {
      setMutationError('A previous delivery is unresolved. Retry that exact unchanged action before creating tickets.');
      return false;
    }
    const attempt = retainedAttempt ?? {
      key: operationKey,
      clientOperationId: crypto.randomUUID()
    };
    operationAttemptsRef.current.set(operationKey, attempt);
    const generation = authorityGenerationRef.current;
    setCommitting(true);
    setMutationError(null);
    try {
      const result = await session.client.commitTicketProposalSet(
        projectId,
        proposalSet.ticketProposalSetId,
        mutationAuthority(attempt.clientOperationId)
      );
      operationAttemptsRef.current.delete(operationKey);
      if (generation !== authorityGenerationRef.current) {
        return true;
      }
      setDeliveryUnresolved(false);
      setCommitDeliveryUnresolved(false);
      setProposalSet(result.proposalSet);
      setCommitResult(result);
      try {
        const nextHistory = await session.client.getTicketProposalSetHistory(
          projectId,
          result.proposalSet.ticketProposalSetId,
          workbenchSession.workbenchSessionId,
          workbenchSession.leaseEpoch
        );
        if (generation === authorityGenerationRef.current) {
          setHistory(nextHistory);
        }
      } catch {
        if (generation === authorityGenerationRef.current) {
          setMutationError('All tickets were created, but revision history could not be refreshed.');
        }
      }
      return true;
    } catch (commitFailure) {
      const definitive = isDefinitiveProposalFailure(commitFailure);
      if (definitive) {
        operationAttemptsRef.current.delete(operationKey);
      }
      if (generation !== authorityGenerationRef.current) {
        return false;
      }
      if (isProposalRevisionConflict(commitFailure)) {
        setDeliveryUnresolved(false);
        setCommitDeliveryUnresolved(false);
        setMutationError('The proposal set changed elsewhere. Reloading the latest durable revision before tickets can be created.');
        setLoadRequest((current) => current + 1);
        return false;
      }
      setDeliveryUnresolved(!definitive);
      setCommitDeliveryUnresolved(!definitive);
      setMutationError(definitive
        ? describeProposalError(commitFailure, 'Ticket creation was rejected. No tickets were created.')
        : 'Ticket creation delivery could not be confirmed. Retry this exact unchanged commit to resolve it safely.');
      return false;
    } finally {
      if (generation === authorityGenerationRef.current) {
        setCommitting(false);
      }
    }
  }, [authorityKey, commitResult, deliveryUnresolved, isCommitting, mutationAuthority, projectId, proposalSet, session.client, workbenchSession]);

  const loadSourceMessage = useCallback(async (messageId: number) => {
    if (!projectId || !Number.isSafeInteger(messageId) || messageId <= 0) {
      return null;
    }
    return session.client.getProjectChatMessage(projectId, messageId);
  }, [projectId, session.client]);

  return {
    proposalSet,
    history,
    loadState,
    error,
    mutationError,
    isMutating,
    isRegenerating,
    isCommitting,
    deliveryUnresolved,
    commitDeliveryUnresolved,
    commitResult,
    loadSourceMessage,
    refresh: () => {
      if (deliveryUnresolved) {
        setMutationError('Retry the exact unresolved review action before refreshing durable state.');
        return;
      }
      setLoadRequest((current) => current + 1);
    },
    updateProposal,
    moveProposal,
    removeProposal,
    resolveIssue,
    regenerate,
    commitTickets
  };
}

function isDefinitiveProposalFailure(error: unknown) {
  if (!(error instanceof IronDevApiError)) {
    return false;
  }
  if (error.status === 400 || error.status === 401 || error.status === 403 ||
      error.status === 404 || error.status === 422) {
    return true;
  }
  const code = readProposalErrorCode(error.body);
  if (error.status === 409) {
    return code !== null && definitiveProposalConflictCodes.has(code);
  }
  return error.status === 503 && code === 'workbench_agent_run_unavailable';
}

function isProposalRevisionConflict(error: unknown) {
  return error instanceof IronDevApiError &&
    error.status === 409 &&
    readProposalErrorCode(error.body) === 'ticket_proposal_revision_conflict';
}

const definitiveProposalConflictCodes = new Set([
  'operation_id_payload_mismatch',
  'ticket_proposal_revision_conflict',
  'ticket_proposal_issue_not_open',
  'ticket_proposal_dependency_invalid',
  'ticket_proposal_final_removal',
  'ticket_proposal_blocking_issues',
  'ticket_proposal_already_committed',
  'ticket_proposal_set_not_ready',
  'ticket_proposal_commit_boundary_invalid',
  'ticket_proposal_project_not_shaping',
  'workbench_lease_fence_rejected',
  'workbench_chat_session_mismatch',
  'workbench_agent_run_active'
]);

function readProposalErrorCode(body: unknown) {
  if (!body || typeof body !== 'object' || !('error' in body)) {
    return null;
  }
  return typeof body.error === 'string' ? body.error : null;
}

function describeProposalError(error: unknown, fallback: string) {
  if (!(error instanceof IronDevApiError)) {
    return fallback;
  }
  const body = error.body;
  if (body && typeof body === 'object' && 'message' in body && typeof body.message === 'string') {
    return body.message;
  }
  return `${fallback} HTTP ${error.status}.`;
}

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => window.setTimeout(resolve, milliseconds));
}
