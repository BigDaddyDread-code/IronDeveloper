import { useCallback, useEffect, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  AcceptProjectRenameProposalRequest,
  ProjectUnderstandingReadModel,
  UpdateProjectUnderstandingFactRequest
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

export type ProjectUnderstandingLoadState = 'idle' | 'loading' | 'ready' | 'error';

interface FactMutationAttempt {
  kind: 'fact';
  identity: string;
  authorityKey: string;
  projectId: number;
  factKey: string;
  request: UpdateProjectUnderstandingFactRequest;
}

interface RenameMutationAttempt {
  kind: 'rename';
  identity: string;
  authorityKey: string;
  projectId: number;
  proposalId: string;
  request: AcceptProjectRenameProposalRequest;
}

type ProjectUnderstandingMutationAttempt = FactMutationAttempt | RenameMutationAttempt;

type FactMutationInput =
  | { action: 'Edit'; value: string }
  | { action: 'Confirm' }
  | { action: 'SetLock'; userLocked: boolean }
  | { action: 'ResolveConflict'; conflictId: string; value: string };

interface UseProjectUnderstandingOptions {
  enabled: boolean;
  terminalAgentRunKey: string | null;
}

export interface ProjectUnderstandingController {
  model: ProjectUnderstandingReadModel | null;
  loadState: ProjectUnderstandingLoadState;
  loadError: string | null;
  mutationError: string | null;
  isMutating: boolean;
  hasUnresolvedMutation: boolean;
  retryLoad: () => void;
  editFact: (factKey: string, value: string) => Promise<boolean>;
  confirmFact: (factKey: string) => Promise<boolean>;
  setFactLock: (factKey: string, userLocked: boolean) => Promise<boolean>;
  resolveConflict: (factKey: string, conflictId: string, value: string) => Promise<boolean>;
  acceptRenameProposal: (proposalId: string) => Promise<boolean>;
  retryPendingMutation: () => Promise<boolean>;
}

export function useProjectUnderstanding({
  enabled,
  terminalAgentRunKey
}: UseProjectUnderstandingOptions): ProjectUnderstandingController {
  const session = useSessionContext();
  const project = useProjectContext();
  const [model, setModel] = useState<ProjectUnderstandingReadModel | null>(null);
  const [loadState, setLoadState] = useState<ProjectUnderstandingLoadState>('idle');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [isMutating, setIsMutating] = useState(false);
  const [loadRequest, setLoadRequest] = useState(0);
  const [hasUnresolvedMutation, setHasUnresolvedMutation] = useState(false);
  const pendingMutationRef = useRef<ProjectUnderstandingMutationAttempt | null>(null);
  const loadGenerationRef = useRef(0);
  const lastTerminalRefreshRef = useRef<string | null>(null);

  const projectId = project.selectedProjectId;
  const workbenchSession = project.workbenchSession;
  const authorityKey = enabled && projectId && workbenchSession
    ? `${projectId}:${workbenchSession.workbenchSessionId}:${workbenchSession.leaseEpoch}`
    : null;
  const authorityKeyRef = useRef(authorityKey);
  authorityKeyRef.current = authorityKey;

  const installModel = useCallback((next: ProjectUnderstandingReadModel) => {
    setModel(next);
    setLoadState('ready');
    setLoadError(null);
    project.applySelectedProjectName(next.projectId, next.projectName);
  }, [project.applySelectedProjectName]);

  const load = useCallback(async (signal?: AbortSignal) => {
    if (!enabled || !projectId || !workbenchSession) {
      setModel(null);
      setLoadState('idle');
      setLoadError(null);
      return;
    }

    const generation = ++loadGenerationRef.current;
    setLoadState((current) => current === 'ready' ? current : 'loading');
    setLoadError(null);
    try {
      const next = await session.client.getProjectUnderstanding(projectId, signal);
      if (signal?.aborted || generation !== loadGenerationRef.current) {
        return;
      }
      installModel(next);
    } catch (error) {
      if (signal?.aborted || isAbortError(error) || generation !== loadGenerationRef.current) {
        return;
      }
      setLoadState('error');
      setLoadError(describeReadError(error));
    }
  }, [enabled, installModel, projectId, session.client, workbenchSession]);

  useEffect(() => {
    pendingMutationRef.current = null;
    setHasUnresolvedMutation(false);
    setMutationError(null);
    setIsMutating(false);
    setModel(null);
    lastTerminalRefreshRef.current = null;
    const controller = new AbortController();
    void load(controller.signal);
    return () => {
      loadGenerationRef.current += 1;
      controller.abort();
    };
  }, [authorityKey, loadRequest]);

  useEffect(() => {
    if (!terminalAgentRunKey || terminalAgentRunKey === lastTerminalRefreshRef.current) {
      return;
    }
    lastTerminalRefreshRef.current = terminalAgentRunKey;
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load, terminalAgentRunKey]);

  const clearPendingMutation = useCallback((attempt: ProjectUnderstandingMutationAttempt) => {
    if (pendingMutationRef.current?.identity !== attempt.identity) {
      return;
    }
    pendingMutationRef.current = null;
    setHasUnresolvedMutation(false);
  }, []);

  const executeMutation = useCallback(async (attempt: ProjectUnderstandingMutationAttempt) => {
    if (pendingMutationRef.current && pendingMutationRef.current.identity !== attempt.identity) {
      setMutationError('A previous project-context change has unresolved delivery. Retry that exact action before changing anything else.');
      setHasUnresolvedMutation(true);
      return false;
    }

    pendingMutationRef.current = attempt;
    setIsMutating(true);
    setMutationError(null);
    try {
      if (attempt.kind === 'fact') {
        const result = await session.client.updateProjectUnderstandingFact(
          attempt.projectId,
          attempt.factKey,
          attempt.request
        );
        if (authorityKeyRef.current !== attempt.authorityKey) {
          return false;
        }
        clearPendingMutation(attempt);
        installModel(result.snapshot);
      } else {
        const result = await session.client.acceptProjectRenameProposal(
          attempt.projectId,
          attempt.proposalId,
          attempt.request
        );
        if (authorityKeyRef.current !== attempt.authorityKey) {
          return false;
        }
        clearPendingMutation(attempt);
        installModel(result.snapshot);
      }
      return true;
    } catch (error) {
      if (authorityKeyRef.current !== attempt.authorityKey) {
        return false;
      }
      if (isDefinitiveMutationRejection(error)) {
        clearPendingMutation(attempt);
        setMutationError(describeMutationError(error));
        if (error instanceof IronDevApiError && error.status === 409) {
          void load();
        }
      } else {
        setHasUnresolvedMutation(true);
        setMutationError('Delivery of this project-context change could not be confirmed. Retry the exact action to reuse its operation ID safely.');
      }
      return false;
    } finally {
      if (authorityKeyRef.current === attempt.authorityKey) {
        setIsMutating(false);
      }
    }
  }, [clearPendingMutation, installModel, load, session.client]);

  const mutateFact = useCallback(async (factKey: string, input: FactMutationInput) => {
    if (!projectId || !workbenchSession || !model || !authorityKey) {
      setMutationError('Project understanding is not ready for changes.');
      return false;
    }

    const normalizedFactKey = factKey.trim();
    const normalizedValue = 'value' in input ? input.value.trim() : null;
    const normalizedConflictId = 'conflictId' in input ? input.conflictId.trim() : null;
    if (!normalizedFactKey || ('value' in input && !normalizedValue) ||
        ('conflictId' in input && !normalizedConflictId)) {
      setMutationError('A fact value is required.');
      return false;
    }

    const actionFields = input.action === 'Edit'
      ? { action: input.action, value: normalizedValue! }
      : input.action === 'Confirm'
        ? { action: input.action }
        : input.action === 'SetLock'
          ? { action: input.action, userLocked: input.userLocked }
          : { action: input.action, conflictId: normalizedConflictId!, value: normalizedValue! };

    const payloadKey = JSON.stringify({
      projectId,
      workbenchSessionId: workbenchSession.workbenchSessionId,
      leaseEpoch: workbenchSession.leaseEpoch,
      revision: model.revision,
      factKey: normalizedFactKey,
      ...actionFields
    });
    const retained = pendingMutationRef.current;
    const clientOperationId = retained?.kind === 'fact' && retained.identity === payloadKey
      ? retained.request.clientOperationId
      : crypto.randomUUID();
    return executeMutation({
      kind: 'fact',
      identity: payloadKey,
      authorityKey,
      projectId,
      factKey: normalizedFactKey,
      request: {
        workbenchSessionId: workbenchSession.workbenchSessionId,
        leaseEpoch: workbenchSession.leaseEpoch,
        expectedUnderstandingRevision: model.revision,
        clientOperationId,
        ...actionFields
      }
    });
  }, [authorityKey, executeMutation, model, projectId, workbenchSession]);

  const editFact = useCallback(
    (factKey: string, value: string) => mutateFact(factKey, { action: 'Edit', value }),
    [mutateFact]
  );
  const confirmFact = useCallback(
    (factKey: string) => mutateFact(factKey, { action: 'Confirm' }),
    [mutateFact]
  );
  const setFactLock = useCallback(
    (factKey: string, userLocked: boolean) => mutateFact(factKey, { action: 'SetLock', userLocked }),
    [mutateFact]
  );
  const resolveConflict = useCallback(
    (factKey: string, conflictId: string, value: string) =>
      mutateFact(factKey, { action: 'ResolveConflict', conflictId, value }),
    [mutateFact]
  );

  const acceptRenameProposal = useCallback(async (proposalId: string) => {
    if (!projectId || !workbenchSession || !model || !authorityKey || !proposalId.trim()) {
      setMutationError('The rename proposal is not ready for acceptance.');
      return false;
    }

    const payloadKey = JSON.stringify({
      projectId,
      workbenchSessionId: workbenchSession.workbenchSessionId,
      leaseEpoch: workbenchSession.leaseEpoch,
      revision: model.revision,
      proposalId
    });
    const retained = pendingMutationRef.current;
    const clientOperationId = retained?.kind === 'rename' && retained.identity === payloadKey
      ? retained.request.clientOperationId
      : crypto.randomUUID();
    return executeMutation({
      kind: 'rename',
      identity: payloadKey,
      authorityKey,
      projectId,
      proposalId,
      request: {
        workbenchSessionId: workbenchSession.workbenchSessionId,
        leaseEpoch: workbenchSession.leaseEpoch,
        clientOperationId
      }
    });
  }, [authorityKey, executeMutation, model, projectId, workbenchSession]);

  const retryPendingMutation = useCallback(async () => {
    const pending = pendingMutationRef.current;
    if (!pending) {
      return false;
    }
    return executeMutation(pending);
  }, [executeMutation]);

  return {
    model,
    loadState,
    loadError,
    mutationError,
    isMutating,
    hasUnresolvedMutation,
    retryLoad: () => setLoadRequest((current) => current + 1),
    editFact,
    confirmFact,
    setFactLock,
    resolveConflict,
    acceptRenameProposal,
    retryPendingMutation
  };
}

function isDefinitiveMutationRejection(error: unknown) {
  if (!(error instanceof IronDevApiError)) {
    return false;
  }
  if (error.status === 400 || error.status === 401 || error.status === 403 || error.status === 404 || error.status === 422) {
    return true;
  }
  if (error.status !== 409) {
    return false;
  }

  const code = readErrorBody(error.body).error;
  return code === 'project_understanding_revision_conflict' ||
    code === 'project_understanding_conflict_not_open' ||
    code === 'workbench_lease_fence_rejected' ||
    code === 'project_rename_proposal_not_pending' ||
    code === 'project_rename_proposal_stale' ||
    code === 'operation_id_payload_mismatch' ||
    code === 'workbench_chat_session_mismatch';
}

function describeReadError(error: unknown) {
  if (error instanceof IronDevApiError && error.status === 404) {
    return 'Project understanding is no longer accessible.';
  }
  return 'Project understanding could not be loaded. The Workshop conversation remains available.';
}

function describeMutationError(error: unknown) {
  if (!(error instanceof IronDevApiError)) {
    return 'The project-context change failed.';
  }
  const body = readErrorBody(error.body);
  if (body.error === 'project_understanding_revision_conflict') {
    return 'Project understanding changed before this action was saved. The latest revision is being loaded for comparison.';
  }
  if (body.error === 'project_understanding_conflict_not_open') {
    return 'This conflict is no longer open. The latest project context is being loaded.';
  }
  if (body.error === 'workbench_lease_fence_rejected') {
    return 'The Workbench write lease changed. Reopen the project before changing its understanding.';
  }
  if (body.error === 'project_rename_proposal_not_pending') {
    return 'This rename proposal is no longer pending. The latest project context is being loaded.';
  }
  if (body.error === 'project_rename_proposal_stale') {
    return 'This suggested name was based on stale project context. The latest proposal is being loaded.';
  }
  if (body.error === 'operation_id_payload_mismatch') {
    return 'The retained operation ID was rejected because its payload changed. Reload Project context before retrying.';
  }
  return body.message ?? `The project-context change was rejected (HTTP ${error.status}).`;
}

function readErrorBody(value: unknown): { error: string | null; message: string | null } {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return { error: null, message: null };
  }
  const body = value as Record<string, unknown>;
  return {
    error: typeof body.error === 'string' ? body.error : null,
    message: typeof body.message === 'string' ? body.message : null
  };
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError';
}
