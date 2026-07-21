import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ActiveWorkbenchSession
} from '../../state/useProjectContext';
import type {
  ConfirmRepositorySetupRequest,
  ProvisionRepositoryRequest,
  RepositoryBindingSnapshot,
  ProjectExecutionProfileSnapshot,
  RepositoryProvisioningAttemptSnapshot,
  RepositoryProvisioningResult,
  RepositorySetupConfirmationResult,
  RepositorySetupContext,
  RepositorySetupPlanPreview,
  RepositorySetupProfileSummary
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface ProjectSetupScreenProps {
  projectId: number;
  entryMode?: boolean;
  onBackToProjects: () => void;
  onOpenBoard: () => void;
}

type LoadState = 'loading' | 'loaded' | 'unavailable';
type BusyAction = 'plan' | 'confirm' | 'provision' | null;

interface PendingConfirmation extends ConfirmRepositorySetupRequest {
  projectId: number;
}

interface PendingProvisioning extends ProvisionRepositoryRequest {
  projectId: number;
}

const pendingConfirmationPrefix = 'irondev.repository-setup-confirmation';
const pendingProvisioningPrefix = 'irondev.repository-provisioning';
const uuidPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

function pendingConfirmationKey(projectId: number, session: ActiveWorkbenchSession): string {
  return `${pendingConfirmationPrefix}:${projectId}:${session.workbenchSessionId}:${session.leaseEpoch}`;
}

function loadPendingConfirmation(projectId: number, session: ActiveWorkbenchSession): PendingConfirmation | null {
  try {
    const raw = window.sessionStorage.getItem(pendingConfirmationKey(projectId, session));
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<PendingConfirmation>;
    return value.projectId === projectId &&
      value.workbenchSessionId === session.workbenchSessionId &&
      value.leaseEpoch === session.leaseEpoch &&
      typeof value.expectedPlanHash === 'string' && /^[0-9a-f]{64}$/i.test(value.expectedPlanHash) &&
      typeof value.clientOperationId === 'string' && uuidPattern.test(value.clientOperationId)
      ? value as PendingConfirmation
      : null;
  } catch {
    return null;
  }
}

function savePendingConfirmation(attempt: PendingConfirmation, session: ActiveWorkbenchSession): boolean {
  try {
    window.sessionStorage.setItem(
      pendingConfirmationKey(attempt.projectId, session),
      JSON.stringify(attempt)
    );
    return true;
  } catch {
    return false;
  }
}

function clearPendingConfirmation(projectId: number, session: ActiveWorkbenchSession): void {
  try {
    window.sessionStorage.removeItem(pendingConfirmationKey(projectId, session));
  } catch {
    // Storage cleanup does not change the authoritative API outcome.
  }
}

function pendingProvisioningKey(projectId: number, session: ActiveWorkbenchSession): string {
  return `${pendingProvisioningPrefix}:${projectId}:${session.workbenchSessionId}:${session.leaseEpoch}`;
}

function loadPendingProvisioning(projectId: number, session: ActiveWorkbenchSession): PendingProvisioning | null {
  try {
    const raw = window.sessionStorage.getItem(pendingProvisioningKey(projectId, session));
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<PendingProvisioning>;
    return value.projectId === projectId &&
      value.workbenchSessionId === session.workbenchSessionId &&
      value.leaseEpoch === session.leaseEpoch &&
      typeof value.clientOperationId === 'string' && uuidPattern.test(value.clientOperationId) &&
      typeof value.setupConfirmationId === 'string' && uuidPattern.test(value.setupConfirmationId) &&
      typeof value.expectedRepositoryBindingRevision === 'number' && Number.isSafeInteger(value.expectedRepositoryBindingRevision) && value.expectedRepositoryBindingRevision > 0 &&
      typeof value.expectedExecutionProfileRevision === 'number' && Number.isSafeInteger(value.expectedExecutionProfileRevision) && value.expectedExecutionProfileRevision > 0
      ? value as PendingProvisioning
      : null;
  } catch {
    return null;
  }
}

function savePendingProvisioning(attempt: PendingProvisioning, session: ActiveWorkbenchSession): boolean {
  try {
    window.sessionStorage.setItem(pendingProvisioningKey(attempt.projectId, session), JSON.stringify(attempt));
    return true;
  } catch {
    return false;
  }
}

function clearPendingProvisioning(projectId: number, session: ActiveWorkbenchSession): void {
  try {
    window.sessionStorage.removeItem(pendingProvisioningKey(projectId, session));
  } catch {
    // Storage cleanup does not change the authoritative API outcome.
  }
}

function isDefinitiveResponse(error: unknown): boolean {
  if (!(error instanceof IronDevApiError) || error.status < 400 || error.status >= 500 || error.status === 408 || error.status === 429) {
    return false;
  }
  if (error.status === 401 || error.status === 403 || error.status === 404) {
    return true;
  }
  if (!error.body || typeof error.body !== 'object' || Array.isArray(error.body)) {
    return false;
  }
  const body = error.body as Record<string, unknown>;
  return [body.reasonCode, body.errorCode, body.code, body.error].some(
    (value) => typeof value === 'string' && value.trim().length > 0
  );
}

function isProvisioningInProgressResponse(error: unknown): boolean {
  if (!(error instanceof IronDevApiError) || error.status !== 409 || !error.body ||
      typeof error.body !== 'object' || Array.isArray(error.body)) {
    return false;
  }
  const body = error.body as Record<string, unknown>;
  return [body.reasonCode, body.errorCode, body.code, body.error].some(
    (value) => typeof value === 'string' && value.trim().toLowerCase() === 'repository_provisioning_in_progress'
  );
}

function isRecoverableProvisioningFenceResponse(error: unknown): boolean {
  if (!(error instanceof IronDevApiError) || error.status !== 409 || !error.body ||
      typeof error.body !== 'object' || Array.isArray(error.body)) {
    return false;
  }
  const body = error.body as Record<string, unknown>;
  return [body.reasonCode, body.errorCode, body.code, body.error].some((value) => {
    if (typeof value !== 'string') return false;
    const code = value.trim().toLowerCase();
    return code === 'workbench_lease_fence_rejected' || code === 'workbench_lease_fence';
  });
}

function describeError(error: unknown, fallback: string): string {
  if (error instanceof IronDevApiError && error.body && typeof error.body === 'object') {
    const body = error.body as { message?: unknown; error?: unknown; nextSafeActions?: unknown };
    const reason = typeof body.message === 'string'
      ? body.message
      : typeof body.error === 'string'
        ? body.error
        : null;
    if (reason) {
      const next = Array.isArray(body.nextSafeActions) && typeof body.nextSafeActions[0] === 'string'
        ? body.nextSafeActions[0]
        : null;
      return next ? `${reason} ${next}` : reason;
    }
  }
  return error instanceof Error && error.message.trim().length > 0 ? error.message : fallback;
}

function compatibilityCopy(profile: RepositorySetupProfileSummary | null): string {
  if (!profile) {
    return 'No compatible v0.1 execution profile is available for the desired technology.';
  }
  if (profile.compatibility === 'Compatible') {
    return profile.compatibilityReason || 'The desired technology matches this pinned v0.1 profile.';
  }
  if (profile.compatibility === 'NeedsConfirmation' || profile.compatibility === 'NoPreference') {
    return profile.compatibilityReason || 'Confirm the desired technology in Workbench before choosing a profile.';
  }
  return profile.compatibilityReason || 'No compatible v0.1 execution profile is available for the desired technology.';
}

export function ProjectSetupScreen({
  projectId,
  entryMode = false,
  onBackToProjects,
  onOpenBoard
}: ProjectSetupScreenProps) {
  const session = useSessionContext();
  const projects = useProjectContext();
  const authority = projects.workbenchSession?.projectId === projectId
    ? projects.workbenchSession
    : null;
  const [context, setContext] = useState<RepositorySetupContext | null>(null);
  const [plan, setPlan] = useState<RepositorySetupPlanPreview | null>(null);
  const [confirmation, setConfirmation] = useState<RepositorySetupConfirmationResult | null>(null);
  const [provisioning, setProvisioning] = useState<RepositoryProvisioningResult | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [busyAction, setBusyAction] = useState<BusyAction>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [ambiguousConfirmation, setAmbiguousConfirmation] = useState(false);
  const [confirmationChecked, setConfirmationChecked] = useState(false);
  const [pendingConfirmation, setPendingConfirmation] = useState<PendingConfirmation | null>(null);
  const [pendingProvisioning, setPendingProvisioning] = useState<PendingProvisioning | null>(null);
  const generationRef = useRef(0);

  useEffect(() => {
    const generation = ++generationRef.current;
    const controller = new AbortController();
    setContext(null);
    setPlan(null);
    setConfirmation(null);
    setProvisioning(null);
    setLoadState('loading');
    setBusyAction(null);
    setErrorMessage(null);
    setAmbiguousConfirmation(false);
    setConfirmationChecked(false);
    const storedConfirmation = authority ? loadPendingConfirmation(projectId, authority) : null;
    const storedProvisioning = authority ? loadPendingProvisioning(projectId, authority) : null;
    setPendingConfirmation(storedConfirmation);
    setPendingProvisioning(storedProvisioning);

    if (!authority) {
      setLoadState('unavailable');
      setErrorMessage('A current Workbench session and write lease are required to configure this repository.');
      return () => {
        controller.abort();
        if (generationRef.current === generation) generationRef.current += 1;
      };
    }

    void session.client.getRepositorySetupContext(projectId, controller.signal)
      .then((result) => {
        if (!controller.signal.aborted && generationRef.current === generation) {
          setContext(result);
          if (!storedProvisioning && result.latestProvisioning?.state === 'Provisioning') {
            const recovered: PendingProvisioning = {
              projectId,
              workbenchSessionId: authority.workbenchSessionId,
              leaseEpoch: authority.leaseEpoch,
              clientOperationId: result.latestProvisioning.clientOperationId,
              setupConfirmationId: result.latestProvisioning.setupConfirmationId,
              expectedRepositoryBindingRevision: result.latestProvisioning.expectedRepositoryBindingRevision,
              expectedExecutionProfileRevision: result.latestProvisioning.expectedExecutionProfileRevision
            };
            if (savePendingProvisioning(recovered, authority)) {
              setPendingProvisioning(recovered);
            } else {
              setErrorMessage('IronDev found an active provisioning operation but could not preserve its exact recovery request in this browser session.');
            }
          }
          setLoadState('loaded');
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted && generationRef.current === generation) {
          setLoadState('unavailable');
          setErrorMessage(describeError(error, 'Repository setup context is unavailable.'));
        }
      });

    return () => {
      controller.abort();
      if (generationRef.current === generation) generationRef.current += 1;
    };
  }, [authority?.leaseEpoch, authority?.workbenchSessionId, projectId, session.client]);

  const selectableProfile = useMemo(
    () => context?.availableProfiles.find((candidate) => candidate.compatibility === 'Compatible') ??
      context?.availableProfiles.find((candidate) => candidate.compatibility === 'NoPreference') ?? null,
    [context]
  );
  const desiredTechnologyProfile = useMemo(
    () => selectableProfile ?? context?.availableProfiles[0] ?? null,
    [selectableProfile, context]
  );
  const technologyNeedsConfirmation = desiredTechnologyProfile?.compatibility === 'NeedsConfirmation';
  const confirmedBinding: RepositoryBindingSnapshot | null = provisioning?.repositoryBinding ?? confirmation?.repositoryBinding ?? context?.repositoryBinding ?? null;
  const executionProfile: ProjectExecutionProfileSnapshot | null = provisioning?.executionProfile ?? confirmation?.executionProfile ?? context?.executionProfile ?? null;
  const setupConfirmationId = confirmation?.confirmationId ?? context?.latestConfirmation?.confirmationId ?? null;
  const latestProvisioning = context?.latestProvisioning ?? null;
  const setupIsConfirmed = confirmedBinding?.bindingState === 'SetupConfirmed';
  const provisioningFailed = confirmedBinding?.bindingState === 'ProvisioningFailed';
  const provisioningInProgress = confirmedBinding?.bindingState === 'Provisioning';
  const repositoryQualified = confirmedBinding?.bindingState === 'Qualified';
  const completeProvisioningAuthority = Boolean(
    (setupIsConfirmed || provisioningFailed) &&
    setupConfirmationId &&
    executionProfile &&
    executionProfile.repositoryBindingId === confirmedBinding?.id
  );
  const pendingMatchesPlan = pendingConfirmation?.expectedPlanHash === plan?.planHash;

  const createPlan = useCallback(async () => {
    if (!authority || !selectableProfile || pendingConfirmation || busyAction !== null) return;
    const generation = generationRef.current;
    setBusyAction('plan');
    setErrorMessage(null);
    setAmbiguousConfirmation(false);
    setConfirmationChecked(false);
    try {
      const result = await session.client.createRepositorySetupPlan(projectId, {
        workbenchSessionId: authority.workbenchSessionId,
        leaseEpoch: authority.leaseEpoch,
        profileDefinitionId: selectableProfile.profileDefinitionId
      });
      if (generationRef.current !== generation) return;
      setPlan(result);
    } catch (error: unknown) {
      if (generationRef.current !== generation) return;
      setPlan(null);
      setErrorMessage(describeError(error, 'IronDev could not create a safe repository setup plan.'));
    } finally {
      if (generationRef.current === generation) setBusyAction(null);
    }
  }, [authority, busyAction, pendingConfirmation, projectId, selectableProfile, session.client]);

  const confirmSetup = useCallback(async () => {
    if (!authority || busyAction !== null) {
      return;
    }

    const isNewConfirmation = pendingConfirmation === null;
    if (isNewConfirmation && (!plan || plan.state !== 'ReadyForConfirmation' || !confirmationChecked)) return;

    const generation = generationRef.current;
    const attempt: PendingConfirmation = pendingConfirmation
      ? pendingConfirmation
      : {
          projectId,
          workbenchSessionId: authority.workbenchSessionId,
          leaseEpoch: authority.leaseEpoch,
          clientOperationId: crypto.randomUUID(),
          expectedPlanHash: plan!.planHash
        };
    if (!savePendingConfirmation(attempt, authority)) {
      setErrorMessage('IronDev could not preserve the exact confirmation operation in this browser session, so no confirmation request was sent.');
      return;
    }
    setPendingConfirmation(attempt);
    setBusyAction('confirm');
    setErrorMessage(null);
    setAmbiguousConfirmation(false);

    try {
      const result = await session.client.confirmRepositorySetup(projectId, {
        workbenchSessionId: attempt.workbenchSessionId,
        leaseEpoch: attempt.leaseEpoch,
        clientOperationId: attempt.clientOperationId,
        expectedPlanHash: attempt.expectedPlanHash
      });
      clearPendingConfirmation(projectId, authority);
      if (generationRef.current !== generation) return;
      setPendingConfirmation(null);
      setConfirmation(result);
      setPlan(null);
      setConfirmationChecked(false);
    } catch (error: unknown) {
      if (isDefinitiveResponse(error)) {
        clearPendingConfirmation(projectId, authority);
      }
      if (generationRef.current !== generation) return;
      const ambiguous = !isDefinitiveResponse(error);
      if (!ambiguous) setPendingConfirmation(null);
      setAmbiguousConfirmation(ambiguous);
      setErrorMessage(ambiguous
        ? 'IronDev could not determine whether setup confirmation was recorded. Retry to replay the exact same operation safely.'
        : describeError(error, 'Repository setup was not confirmed.'));
    } finally {
      if (generationRef.current === generation) setBusyAction(null);
    }
  }, [authority, busyAction, confirmationChecked, pendingConfirmation, plan, projectId, session.client]);

  const provisionRepository = useCallback(async () => {
    if (!authority || busyAction !== null) return;

    const isNewProvisioning = pendingProvisioning === null;
    if (isNewProvisioning && (!completeProvisioningAuthority || !confirmedBinding || !executionProfile || !setupConfirmationId)) {
      return;
    }

    const generation = generationRef.current;
    const attempt: PendingProvisioning = pendingProvisioning
      ? pendingProvisioning
      : {
          projectId,
          workbenchSessionId: authority.workbenchSessionId,
          leaseEpoch: authority.leaseEpoch,
          clientOperationId: crypto.randomUUID(),
          setupConfirmationId: setupConfirmationId!,
          expectedRepositoryBindingRevision: confirmedBinding!.revision,
          expectedExecutionProfileRevision: executionProfile!.revision
        };

    if (!savePendingProvisioning(attempt, authority)) {
      setErrorMessage('IronDev could not preserve the exact provisioning operation in this browser session, so no provisioning request was sent.');
      return;
    }

    setPendingProvisioning(attempt);
    setBusyAction('provision');
    setErrorMessage(null);

    try {
      const result = await session.client.provisionRepository(projectId, {
        workbenchSessionId: attempt.workbenchSessionId,
        leaseEpoch: attempt.leaseEpoch,
        clientOperationId: attempt.clientOperationId,
        setupConfirmationId: attempt.setupConfirmationId,
        expectedRepositoryBindingRevision: attempt.expectedRepositoryBindingRevision,
        expectedExecutionProfileRevision: attempt.expectedExecutionProfileRevision
      });
      clearPendingProvisioning(projectId, authority);
      if (generationRef.current !== generation) return;
      setPendingProvisioning(null);
      setProvisioning(result);
      setErrorMessage(null);
    } catch (error: unknown) {
      const inProgress = isProvisioningInProgressResponse(error);
      const recoverableFence = isRecoverableProvisioningFenceResponse(error);
      const definitive = isDefinitiveResponse(error) && !inProgress && !recoverableFence;
      if (definitive) clearPendingProvisioning(projectId, authority);
      if (generationRef.current !== generation) return;
      if (definitive) {
        setPendingProvisioning(null);
        setConfirmation(null);
        setProvisioning(null);
        setContext(null);
        try {
          const refreshed = await session.client.getRepositorySetupContext(projectId);
          if (generationRef.current === generation) {
            setContext(refreshed);
            setLoadState('loaded');
          }
        } catch {
          if (generationRef.current === generation) setLoadState('unavailable');
        }
      }
      if (generationRef.current !== generation) return;
      const ambiguous = !definitive;
      setErrorMessage(ambiguous
        ? inProgress
          ? 'This exact repository provisioning operation is already in progress. Retry the same operation to recover its authoritative result; do not start a different operation.'
          : recoverableFence
            ? 'Workbench write authority changed while this exact repository provisioning operation was active. Retry the same operation after the current Workbench session is established; do not start a different operation.'
          : 'IronDev could not determine whether repository provisioning completed. Retry to replay the exact same operation safely.'
        : describeError(error, 'Repository provisioning did not complete.'));
    } finally {
      if (generationRef.current === generation) setBusyAction(null);
    }
  }, [
    authority,
    busyAction,
    completeProvisioningAuthority,
    confirmedBinding,
    executionProfile,
    pendingProvisioning,
    projectId,
    session.client,
    setupConfirmationId
  ]);

  const returnToConfiguration = () => {
    setPlan(null);
    setConfirmationChecked(false);
    setErrorMessage(null);
    setAmbiguousConfirmation(false);
  };

  const projectName = context?.projectName ?? projects.selectedProjectName ?? `Project ${projectId}`;
  const status = repositoryQualified
    ? 'Repository qualified'
    : provisioningInProgress || busyAction === 'provision'
      ? 'Provisioning'
    : provisioningFailed
      ? 'Provisioning failed'
    : setupIsConfirmed
      ? 'Setup confirmed'
    : plan?.state === 'ReadyForConfirmation'
      ? 'Awaiting confirmation'
      : loadState === 'unavailable'
        ? 'Unavailable'
        : 'Not configured';

  return (
    <main
      className={`fl-root fl-project-setup fl-repository-setup${entryMode ? ' fl-project-setup--entry' : ''}`}
      data-testid="flow.projectSetup"
    >
      <header className="fl-project-setup__header">
        <button className="fl-btn fl-project-setup__back" type="button" onClick={onBackToProjects}>
          Back to projects
        </button>
        <div className="fl-project-setup__identity">
          <span className="fl-eyebrow">Repository</span>
          <h1 className="fl-h1">Configure repository</h1>
          <p className="fl-project-setup__path">{projectName}</p>
        </div>
        <span className={`fl-project-setup__status fl-project-setup__status--${repositoryQualified ? 'ready' : loadState === 'unavailable' || provisioningFailed ? 'unavailable' : 'attention'}`}>
          {status}
        </span>
      </header>

      <div className="fl-project-setup__body">
        <nav className="fl-repository-setup__steps" aria-label="Repository setup steps">
          <span>Repository</span><span aria-hidden="true">-&gt;</span>
          <span>Configure repository</span><span aria-hidden="true">-&gt;</span>
          <strong>Create inside IronDev workspace</strong>
        </nav>

        {loadState === 'loading' ? (
          <section className="fl-project-setup__next fl-project-setup__next--loading" aria-label="Loading repository setup">
            <div className="fl-project-setup__skeleton" />
            <div className="fl-project-setup__skeleton fl-project-setup__skeleton--short" />
          </section>
        ) : loadState === 'unavailable' ? (
          <section className="fl-project-setup__unavailable" data-testid="repositorySetup.unavailable">
            <h2>Repository setup is unavailable</h2>
            <p>This does not block Workbench shaping or ticket creation.</p>
            {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
            <button className="fl-btn" type="button" onClick={onOpenBoard}>Continue shaping the project</button>
          </section>
        ) : pendingConfirmation && (!plan || !pendingMatchesPlan) ? (
          <AmbiguousRecovery
            attempt={pendingConfirmation}
            busy={busyAction === 'confirm'}
            errorMessage={errorMessage}
            onRetry={() => void confirmSetup()}
          />
        ) : pendingProvisioning ? (
          <ProvisioningRecovery
            attempt={pendingProvisioning}
            active={latestProvisioning?.state === 'Provisioning' && latestProvisioning.clientOperationId === pendingProvisioning.clientOperationId}
            busy={busyAction === 'provision'}
            errorMessage={errorMessage}
            onRetry={() => void provisionRepository()}
          />
        ) : repositoryQualified ? (
          <QualifiedRepository
            binding={confirmedBinding!}
            provisioning={provisioning}
            latestAttempt={latestProvisioning}
            lifecycle={provisioning?.projectLifecyclePhase ?? context?.projectLifecyclePhase ?? 'Shaping'}
            readiness={provisioning?.executionReadiness ?? context?.executionReadiness ?? 'NotConfigured'}
            onContinue={onOpenBoard}
          />
        ) : provisioningInProgress ? (
          <ProvisioningProgress latestAttempt={latestProvisioning} errorMessage={errorMessage} onContinue={onOpenBoard} />
        ) : setupIsConfirmed || provisioningFailed ? (
          <ConfirmedSetup
            binding={confirmedBinding!}
            profile={executionProfile!}
            confirmationId={setupConfirmationId!}
            latestAttempt={latestProvisioning}
            lifecycle={confirmation?.projectLifecyclePhase ?? context?.projectLifecyclePhase ?? 'Shaping'}
            readiness={confirmation?.executionReadiness ?? context?.executionReadiness ?? 'NotConfigured'}
            busy={busyAction === 'provision'}
            errorMessage={errorMessage}
            onProvision={() => void provisionRepository()}
            onContinue={onOpenBoard}
          />
        ) : confirmedBinding?.bindingState === 'LegacyUnverified' ? (
          <section className="fl-project-setup__unavailable" data-testid="repositorySetup.legacy">
            <h2>Existing repository binding needs verification</h2>
            <p>The legacy path has not been accepted as trusted repository authority. Existing-project verification belongs to the next repository slice.</p>
            <p>Workbench shaping and ticket creation remain available.</p>
            <button className="fl-btn" type="button" onClick={onOpenBoard}>Continue shaping the project</button>
          </section>
        ) : plan ? (
          <PlanSurface
            plan={plan}
            busy={busyAction === 'confirm'}
            checked={confirmationChecked}
            locked={Boolean(pendingConfirmation)}
            ambiguous={ambiguousConfirmation || pendingMatchesPlan}
            errorMessage={errorMessage}
            onChecked={setConfirmationChecked}
            onConfirm={() => void confirmSetup()}
            onCancel={returnToConfiguration}
            onContinue={onOpenBoard}
          />
        ) : (
          <section className="fl-repository-setup__configure" data-testid="repositorySetup.configure">
            <div className="fl-project-setup__intro">
              <p>Create and confirm a deterministic setup plan first. Provisioning remains a separate explicit action.</p>
            </div>

            <div className="fl-repository-setup__capabilities">
              <article data-testid="repositorySetup.compatibility">
                <span className="fl-eyebrow">Desired technology compatibility</span>
                <h2>{selectableProfile
                  ? 'v0.1 profile available for explicit selection'
                  : technologyNeedsConfirmation
                    ? 'Confirm the desired technology in Workbench'
                    : 'No compatible v0.1 profile'}</h2>
                <p>{compatibilityCopy(desiredTechnologyProfile)}</p>
                {desiredTechnologyProfile ? (
                  <dl>
                    <div><dt>Profile</dt><dd>{desiredTechnologyProfile.displayName}</dd></div>
                    <div><dt>Profile ID</dt><dd><code>{desiredTechnologyProfile.profileDefinitionId}</code></dd></div>
                    <div><dt>Compatibility</dt><dd>{desiredTechnologyProfile.compatibility}</dd></div>
                  </dl>
                ) : null}
              </article>

              <article data-testid="repositorySetup.capability">
                <span className="fl-eyebrow">Environment and artifact capability</span>
                <h2>{context?.environmentCapability.state ?? 'Unavailable'}</h2>
                <p>{context?.environmentCapability.message ?? 'Repository setup capability was not reported.'}</p>
                {context?.environmentCapability.suggestedTarget ? (
                  <dl><div><dt>Suggested target</dt><dd>{context.environmentCapability.suggestedTarget}</dd></div></dl>
                ) : null}
                <p>{desiredTechnologyProfile
                  ? `${desiredTechnologyProfile.planningReadiness} / ${desiredTechnologyProfile.certificationState}. A preview is not execution certification.`
                  : 'No pinned profile artifacts can be planned for the desired technology.'}</p>
              </article>
            </div>

            {technologyNeedsConfirmation ? (
              <section className="fl-project-setup__unavailable" data-testid="repositorySetup.needsTechnology">
                <h2>Confirm the desired technology first</h2>
                <p>{compatibilityCopy(desiredTechnologyProfile)}</p>
                <p>Return to Workbench to resolve the technology preference. No profile will be substituted.</p>
                <p>You can continue shaping the project and creating tickets.</p>
                <button className="fl-btn" type="button" onClick={onOpenBoard}>Continue shaping the project</button>
              </section>
            ) : !selectableProfile ? (
              <section className="fl-project-setup__unavailable" data-testid="repositorySetup.unsupported">
                <h2>No v0.1 execution profile is available for this technology</h2>
                <p>IronDev will not substitute WinForms or another implementation profile.</p>
                <p>You can continue shaping the project and creating tickets.</p>
                <button className="fl-btn" type="button" onClick={onOpenBoard}>Continue shaping the project</button>
              </section>
            ) : (
              <section className="fl-project-setup__next" data-testid="repositorySetup.create">
                <h2>Create inside IronDev workspace</h2>
                <p>Review the pinned path, profile, artifacts, commands, Git initialization, indexing, sandbox validation, and resource policy before confirming.</p>
                <button
                  className="fl-btn fl-pri"
                  type="button"
                  data-testid="repositorySetup.reviewPlan"
                  disabled={busyAction !== null}
                  onClick={() => void createPlan()}
                >
                  {busyAction === 'plan' ? 'Creating setup plan...' : 'Review setup plan'}
                </button>
              </section>
            )}
            {errorMessage ? (
              <div className="fl-error" role="alert">
                {errorMessage} No repository directory was created.
              </div>
            ) : null}
          </section>
        )}
      </div>
    </main>
  );
}

function PlanSurface({
  plan,
  busy,
  checked,
  locked,
  ambiguous,
  errorMessage,
  onChecked,
  onConfirm,
  onCancel,
  onContinue
}: {
  plan: RepositorySetupPlanPreview;
  busy: boolean;
  checked: boolean;
  locked: boolean;
  ambiguous: boolean;
  errorMessage: string | null;
  onChecked: (checked: boolean) => void;
  onConfirm: () => void;
  onCancel: () => void;
  onContinue: () => void;
}) {
  if (plan.state === 'UnsupportedProfile') {
    return (
      <section className="fl-project-setup__unavailable" data-testid="repositorySetup.planUnsupported">
        <h2>No v0.1 execution profile is available for this technology</h2>
        <p>{plan.message}</p>
        <p>IronDev will not substitute WinForms. Workbench shaping and ticket creation can continue.</p>
        <button className="fl-btn" type="button" onClick={onCancel}>Back to repository setup</button>
        <button className="fl-btn" type="button" onClick={onContinue}>Continue shaping the project</button>
      </section>
    );
  }

  if (plan.state === 'EnvironmentUnavailable') {
    return (
      <section className="fl-project-setup__unavailable" data-testid="repositorySetup.environmentUnavailable">
        <h2>Greenfield provisioning is unavailable in this environment</h2>
        <p>{plan.message}</p>
        <p>The required isolated execution environment or pinned artifacts are not configured.</p>
        <p>You can continue shaping the project and creating tickets.</p>
        <button className="fl-btn" type="button" onClick={onCancel}>Back to repository setup</button>
        <button className="fl-btn" type="button" onClick={onContinue}>Continue shaping the project</button>
      </section>
    );
  }

  if (plan.state === 'NeedsConfirmation') {
    return (
      <section className="fl-project-setup__unavailable" data-testid="repositorySetup.needsTechnology">
        <h2>Confirm the desired technology first</h2>
        <p>{plan.message}</p>
        <p>Return to Workbench to resolve the technology preference. No profile will be substituted.</p>
        <button className="fl-btn" type="button" onClick={onCancel}>Back to repository setup</button>
        <button className="fl-btn" type="button" onClick={onContinue}>Continue shaping the project</button>
      </section>
    );
  }

  return (
    <section className="fl-project-setup__next fl-repository-setup__review" data-testid="repositorySetup.review">
      <span className="fl-eyebrow">Setup plan</span>
      <h2>Review before confirmation</h2>
      <p>{plan.message}</p>

      <dl className="fl-repository-setup__plan">
        <PlanRow label="Plan schema" value={`${plan.schemaVersion} (${plan.source})`} />
        <PlanRow label="Canonical project name" value={plan.canonicalProjectName} />
        <PlanRow label="Workbench session" value={String(plan.workbenchSessionId)} />
        <PlanRow label="Lease epoch" value={String(plan.leaseEpoch)} />
        <PlanRow label="Understanding revision" value={String(plan.basedOnUnderstandingRevision)} />
        <PlanRow label="Understanding SHA-256" value={plan.basedOnUnderstandingHash} code />
        <PlanRow label="Profile descriptor revision" value={String(plan.profileDescriptorRevision)} />
        <PlanRow label="Profile descriptor SHA-256" value={plan.profileDescriptorSha256} code />
        <PlanRow label="Target path" value={plan.targetPath} testId="repositorySetup.plan.target" />
        <PlanRow label="Pinned profile" value={`${plan.profile.displayName} (${plan.profile.profileDefinitionId})`} />
        <PlanRow label="Solution" value={plan.solutionName} />
        <PlanRow label="Application project" value={plan.appProjectName} />
        <PlanRow label="Test project" value={plan.testProjectName} />
        <PlanRow label="Solution path" value={plan.solutionPath} />
        <PlanRow label="Application project path" value={plan.appProjectPath} />
        <PlanRow label="Test project path" value={plan.testProjectPath} />
        <PlanRow label="Technology" value={`${plan.language} / ${plan.targetFramework} / ${plan.applicationKind} / ${plan.testFramework}`} />
        <PlanRow label="SDK / runtime" value={`${plan.sdkVersion} / ${plan.runtimeVersion}`} />
        <PlanRow label="Template bundle SHA-256" value={plan.templateBundleSha256} code />
        <PlanRow label="Planning bundle / image SHA-256" value={plan.planningBundleSha256} code />
        <PlanRow label="Toolchain manifest" value={plan.toolchainManifestId} code />
        <PlanRow label="Planned execution image" value={plan.executionImageReference} code />
        <PlanRow label="Default branch" value={plan.defaultBranch} code />
        <PlanRow label="Restore command" value={plan.restoreCommand} code />
        <PlanRow label="Build command" value={plan.buildCommand} code />
        <PlanRow label="Test command" value={plan.testCommand} code />
        <PlanRow label="Initialize Git" value={plan.initializeGit ? 'Yes' : 'No'} />
        <PlanRow label="Index after provisioning" value={plan.indexAfterProvisioning ? 'Yes' : 'No'} />
        <PlanRow label="Sandbox validation" value={plan.sandboxValidation} />
        <PlanRow label="Resource policy" value={plan.resourcePolicy} />
        <PlanRow label="Plan hash" value={plan.planHash} code testId="repositorySetup.plan.hash" />
      </dl>

      <div className="fl-project-setup__safety-boundary">
        <strong>Confirmation boundary</strong>
        <p>{plan.profile.planningReadiness} / {plan.profile.certificationState}. This records repository and profile setup authority only. Provisioning does not run, no directory is created, execution readiness stays NotConfigured, and Builder is not authorized.</p>
      </div>

      <label className="fl-project-setup__confirmation">
        <input
          type="checkbox"
          checked={checked}
          disabled={locked}
          onChange={(event) => onChecked(event.currentTarget.checked)}
          data-testid="repositorySetup.confirmCheck"
        />
        I confirm this exact hashed setup plan.
      </label>

      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      {locked ? (
        <p data-testid="repositorySetup.ambiguousLock">The prior delivery is unresolved. Only an exact retry is allowed; this plan cannot be cancelled or changed.</p>
      ) : null}
      <div className="fl-project-setup__task-actions">
        {!locked ? <button className="fl-btn" type="button" disabled={busy} onClick={onCancel}>Cancel</button> : null}
        <button
          className="fl-btn fl-pri"
          type="button"
          data-testid="repositorySetup.confirm"
          disabled={!checked || busy}
          onClick={onConfirm}
        >
          {busy ? 'Confirming...' : ambiguous ? 'Retry same confirmation' : 'Confirm repository setup'}
        </button>
      </div>
    </section>
  );
}

function AmbiguousRecovery({
  attempt,
  busy,
  errorMessage,
  onRetry
}: {
  attempt: PendingConfirmation;
  busy: boolean;
  errorMessage: string | null;
  onRetry: () => void;
}) {
  return (
    <section className="fl-project-setup__next" data-testid="repositorySetup.recoverConfirmation">
      <span className="fl-eyebrow">Unresolved confirmation</span>
      <h2>Retry the exact repository setup confirmation</h2>
      <p>IronDev could not determine whether the prior request committed. No new plan or operation can replace it until this exact operation receives an authoritative response.</p>
      <dl className="fl-repository-setup__plan">
        <PlanRow label="Operation ID" value={attempt.clientOperationId} code testId="repositorySetup.recovery.operation" />
        <PlanRow label="Expected plan hash" value={attempt.expectedPlanHash} code testId="repositorySetup.recovery.hash" />
        <PlanRow label="Workbench session" value={String(attempt.workbenchSessionId)} />
        <PlanRow label="Lease epoch" value={String(attempt.leaseEpoch)} />
      </dl>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <div className="fl-project-setup__task-actions">
        <button
          className="fl-btn fl-pri"
          type="button"
          data-testid="repositorySetup.retryExact"
          disabled={busy}
          onClick={onRetry}
        >
          {busy ? 'Retrying...' : 'Retry same confirmation'}
        </button>
      </div>
    </section>
  );
}

function PlanRow({ label, value, code = false, testId }: { label: string; value: string; code?: boolean; testId?: string }) {
  return (
    <div data-testid={testId}>
      <dt>{label}</dt>
      <dd>{code ? <code>{value}</code> : value}</dd>
    </div>
  );
}

function ConfirmedSetup({
  binding,
  profile,
  confirmationId,
  latestAttempt,
  lifecycle,
  readiness,
  busy,
  errorMessage,
  onProvision,
  onContinue
}: {
  binding: RepositoryBindingSnapshot;
  profile: ProjectExecutionProfileSnapshot;
  confirmationId: string;
  latestAttempt: RepositoryProvisioningAttemptSnapshot | null;
  lifecycle: string;
  readiness: string;
  busy: boolean;
  errorMessage: string | null;
  onProvision: () => void;
  onContinue: () => void;
}) {
  const retry = binding.bindingState === 'ProvisioningFailed';
  return (
    <section className="fl-project-setup__ready fl-repository-setup__confirmed" data-testid="repositorySetup.confirmed">
      <p className="fl-project-setup__ready-mark" aria-hidden="true">{retry ? '!' : 'OK'}</p>
      <h2>{retry ? 'Repository provisioning failed safely' : 'Repository setup confirmed'}</h2>
      <p>{retry
        ? 'The failed attempt is durable. The unknown or incomplete target was not adopted, and a new exact operation may retry from the current authority.'
        : 'The exact setup authority was recorded. Provisioning has not run.'}</p>
      <dl>
        <div><dt>Binding state</dt><dd>{binding.bindingState}</dd></div>
        <div><dt>Planned target</dt><dd>{binding.canonicalPath}</dd></div>
        <div><dt>Setup confirmation</dt><dd><code>{confirmationId}</code></dd></div>
        <div><dt>Binding revision</dt><dd>{binding.revision}</dd></div>
        <div><dt>Profile revision</dt><dd>{profile.revision}</dd></div>
        {latestAttempt?.failureCode ? <div><dt>Failure code</dt><dd><code>{latestAttempt.failureCode}</code></dd></div> : null}
        <div><dt>Project lifecycle</dt><dd>{lifecycle}</dd></div>
        <div><dt>Execution readiness</dt><dd>{readiness}</dd></div>
      </dl>
      <div className="fl-project-setup__safety-boundary">
        <strong>Provisioning boundary</strong>
        <p>The repository directory was not created by setup confirmation. Provisioning creates only the pinned source shell and a clean Git repository. It does not restore, build, test, index code, run sandbox validation, or authorize Builder. Builder is not authorized, and execution readiness remains NotConfigured.</p>
      </div>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <div className="fl-project-setup__task-actions">
        <button className="fl-btn" type="button" disabled={busy} onClick={onContinue}>Continue to project</button>
        <button
          className="fl-btn fl-pri"
          type="button"
          data-testid="repositorySetup.provision"
          disabled={busy}
          onClick={onProvision}
        >
          {busy ? 'Provisioning repository...' : retry ? 'Retry repository provisioning' : 'Provision repository'}
        </button>
      </div>
    </section>
  );
}

function ProvisioningRecovery({
  attempt,
  active,
  busy,
  errorMessage,
  onRetry
}: {
  attempt: PendingProvisioning;
  active: boolean;
  busy: boolean;
  errorMessage: string | null;
  onRetry: () => void;
}) {
  return (
    <section className="fl-project-setup__next" data-testid="repositorySetup.recoverProvisioning">
      <span className="fl-eyebrow">Unresolved provisioning delivery</span>
      <h2>{active ? 'Repository provisioning is in progress' : 'Retry the exact repository provisioning operation'}</h2>
      <p>{active
        ? 'The durable attempt is active. Retry its exact operation with the current Workbench fence to recover the authoritative result.'
        : 'IronDev could not determine whether the prior operation completed. No different target, plan, revision, or operation can replace it in this browser session.'}</p>
      <dl className="fl-repository-setup__plan">
        <PlanRow label="Operation ID" value={attempt.clientOperationId} code testId="repositorySetup.provisioningRecovery.operation" />
        <PlanRow label="Setup confirmation" value={attempt.setupConfirmationId} code />
        <PlanRow label="Expected binding revision" value={String(attempt.expectedRepositoryBindingRevision)} />
        <PlanRow label="Expected profile revision" value={String(attempt.expectedExecutionProfileRevision)} />
        <PlanRow label="Workbench session" value={String(attempt.workbenchSessionId)} />
        <PlanRow label="Lease epoch" value={String(attempt.leaseEpoch)} />
      </dl>
      <div className="fl-project-setup__safety-boundary">
        <strong>Exact replay only</strong>
        <p>The request contains no browser-supplied path, template, command, branch, or cleanup policy.</p>
      </div>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <button
        className="fl-btn fl-pri"
        type="button"
        data-testid="repositorySetup.retryProvisioning"
        disabled={busy}
        onClick={onRetry}
      >
        {busy ? 'Retrying exact operation...' : 'Retry same provisioning operation'}
      </button>
    </section>
  );
}

function ProvisioningProgress({
  latestAttempt,
  errorMessage,
  onContinue
}: {
  latestAttempt: RepositoryProvisioningAttemptSnapshot | null;
  errorMessage: string | null;
  onContinue: () => void;
}) {
  return (
    <section className="fl-project-setup__next" data-testid="repositorySetup.provisioningProgress">
      <span className="fl-eyebrow">Controlled provisioning</span>
      <h2>Repository provisioning is in progress</h2>
      <p>IronDev has claimed this exact confirmed authority. A second provisioning operation is not available while it is active.</p>
      {latestAttempt ? (
        <dl className="fl-repository-setup__plan">
          <PlanRow label="Attempt" value={String(latestAttempt.attemptNumber)} />
          <PlanRow label="Attempt ID" value={latestAttempt.attemptId} code />
          <PlanRow label="Started" value={latestAttempt.startedAtUtc} />
        </dl>
      ) : null}
      <div className="fl-project-setup__safety-boundary">
        <strong>Source and Git only</strong>
        <p>No restore, build, test, index, sandbox validation, or Builder authorization runs in this operation.</p>
      </div>
      {errorMessage ? <div className="fl-error" role="alert">{errorMessage}</div> : null}
      <button className="fl-btn" type="button" onClick={onContinue}>Continue to project</button>
    </section>
  );
}

function QualifiedRepository({
  binding,
  provisioning,
  latestAttempt,
  lifecycle,
  readiness,
  onContinue
}: {
  binding: RepositoryBindingSnapshot;
  provisioning: RepositoryProvisioningResult | null;
  latestAttempt: RepositoryProvisioningAttemptSnapshot | null;
  lifecycle: string;
  readiness: string;
  onContinue: () => void;
}) {
  const branchName = provisioning?.branchName ?? latestAttempt?.branchName ?? binding.defaultBranch ?? '';
  const baselineCommit = provisioning?.baselineCommit ?? latestAttempt?.baselineCommit ?? binding.baselineCommit ?? '';
  return (
    <section className="fl-project-setup__ready fl-repository-setup__confirmed" data-testid="repositorySetup.qualified">
      <p className="fl-project-setup__ready-mark" aria-hidden="true">OK</p>
      <h2>Repository shell provisioned</h2>
      <p>The pinned source shell and controlled Git baseline were installed at the server-derived target.</p>
      <dl>
        <div><dt>Binding state</dt><dd>{binding.bindingState}</dd></div>
        <div><dt>Repository / projected local path</dt><dd>{binding.canonicalPath}</dd></div>
        <div><dt>Default branch</dt><dd><code>{branchName}</code></dd></div>
        <div><dt>Baseline commit</dt><dd><code>{baselineCommit}</code></dd></div>
        {provisioning ? <div><dt>Manifest SHA-256</dt><dd><code>{provisioning.manifestSha256}</code></dd></div> : null}
        {provisioning ? <div><dt>Git tree ID</dt><dd><code>{provisioning.gitTreeId}</code></dd></div> : null}
        <div><dt>Project lifecycle</dt><dd>{lifecycle}</dd></div>
        <div><dt>Execution readiness</dt><dd>{readiness}</dd></div>
      </dl>
      <div className="fl-project-setup__safety-boundary">
        <strong>Technical readiness is still separate</strong>
        <p>Qualified means the repository artifact and Git baseline were verified. IronDev has not restored, built, tested, indexed, run sandbox validation, or authorized Builder. Execution readiness remains NotConfigured.</p>
      </div>
      <button className="fl-btn fl-pri" type="button" onClick={onContinue}>Continue to project</button>
    </section>
  );
}
