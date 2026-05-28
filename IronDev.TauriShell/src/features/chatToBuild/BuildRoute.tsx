import { useCallback, useEffect, useMemo, useState } from 'react';
import type { WorkspaceCommand, WorkspaceRoute, WorkspaceRouteMeta, WorkspaceSummaryChip } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  CreateTicketFromDocumentResponse,
  ProjectTicket,
  RunReviewPackage,
  RunTicketReviewResponse,
  SaveDiscussionResponse,
  StartDisposableCodeRunResponse
} from '../../api/types';
import { Surface } from '../../design-system/Surface';
import { StatusBadge } from '../../components/StatusBadge';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { DiscussionComposer } from './DiscussionComposer';
import { DiscussionDocumentCard } from './DiscussionDocumentCard';
import { DisposableRunPanel } from './DisposableRunPanel';
import { FlowStageRail, type FlowStageItem } from './FlowStageRail';
import { GeneratedTicketPanel } from './GeneratedTicketPanel';
import { RunReviewPackagePanel } from './RunReviewPackagePanel';
import { TicketReviewPanel } from './TicketReviewPanel';

type ChatBuildBusyState = 'saveDiscussion' | 'createTicket' | 'reviewTicket' | 'startRun' | 'loadPackage' | null;

interface BuildRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const defaultTitle = 'Project build request';

export function BuildRoute({ route, onRouteReady }: BuildRouteProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const [title, setTitle] = useState(defaultTitle);
  const [content, setContent] = useState('');
  const [document, setDocument] = useState<SaveDiscussionResponse | null>(null);
  const [ticket, setTicket] = useState<CreateTicketFromDocumentResponse | null>(null);
  const [ticketDetail, setTicketDetail] = useState<ProjectTicket | null>(null);
  const [review, setReview] = useState<RunTicketReviewResponse | null>(null);
  const [run, setRun] = useState<StartDisposableCodeRunResponse | null>(null);
  const [reviewPackage, setReviewPackage] = useState<RunReviewPackage | null>(null);
  const [busyState, setBusyState] = useState<ChatBuildBusyState>(null);
  const [statusMessage, setStatusMessage] = useState('Ready.');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const projectId = project.selectedProjectId;
  const isBusy = busyState !== null;
  const accessBlockedReason = getAccessBlockedReason(session.tokenConfigured, projectId, project.accessStatus);
  const discussionBlockedReason = accessBlockedReason ?? getDiscussionBlockedReason(title, content);
  const ticketBlockedReason = accessBlockedReason ?? (!document ? 'Save a discussion document first.' : null);
  const reviewBlockedReason = accessBlockedReason ?? (!ticket ? 'Create a ticket first.' : null);
  const runBlockedReason =
    accessBlockedReason ??
    (!ticket ? 'Create a ticket first.' : !review ? 'Review the ticket first.' : review.result.decision.proceed ? null : 'Ticket review did not approve proceeding.');
  const packageBlockedReason = accessBlockedReason ?? (!ticket || !run ? 'Start a sandbox code run first.' : null);

  const resetFromDocument = useCallback(() => {
    setTicket(null);
    setTicketDetail(null);
    setReview(null);
    setRun(null);
    setReviewPackage(null);
  }, []);

  const resetAll = useCallback(() => {
    setTitle(defaultTitle);
    setContent('');
    setDocument(null);
    setTicket(null);
    setTicketDetail(null);
    setReview(null);
    setRun(null);
    setReviewPackage(null);
    setBusyState(null);
    setStatusMessage('Ready.');
    setErrorMessage(null);
  }, []);

  const saveDiscussion = useCallback(async () => {
    if (!projectId || discussionBlockedReason) {
      return;
    }

    setBusyState('saveDiscussion');
    setErrorMessage(null);
    resetFromDocument();

    try {
      const response = await session.client.saveDiscussion(projectId, {
        title: title.trim(),
        content: content.trim()
      });
      setDocument(response);
      setStatusMessage(`Saved discussion document ${response.documentId}.`);
    } catch (error) {
      setErrorMessage(describeApiError(error, 'Save discussion failed.'));
    } finally {
      setBusyState(null);
    }
  }, [content, discussionBlockedReason, projectId, resetFromDocument, session.client, title]);

  const createTicket = useCallback(async () => {
    if (!projectId || !document || ticketBlockedReason) {
      return;
    }

    setBusyState('createTicket');
    setErrorMessage(null);
    setReview(null);
    setRun(null);
    setReviewPackage(null);
    setTicketDetail(null);

    try {
      const response = await session.client.createTicketFromDocument(projectId, document.documentVersionId, {});
      setTicket(response);
      try {
        const detail = await session.client.getProjectTicket(projectId, response.ticketId);
        setTicketDetail(detail);
      } catch (detailError) {
        setErrorMessage(describeApiError(detailError, 'Ticket was created, but detail could not be loaded.'));
      }
      setStatusMessage(`Created ticket ${response.ticketId}.`);
    } catch (error) {
      setErrorMessage(describeApiError(error, 'Create ticket failed.'));
    } finally {
      setBusyState(null);
    }
  }, [document, projectId, session.client, ticketBlockedReason]);

  const reviewTicket = useCallback(async () => {
    if (!projectId || !ticket || reviewBlockedReason) {
      return;
    }

    setBusyState('reviewTicket');
    setErrorMessage(null);
    setRun(null);
    setReviewPackage(null);

    try {
      const response = await session.client.reviewTicket(projectId, ticket.ticketId, { useLiveModel: false });
      setReview(response);
      setStatusMessage(`Reviewed ticket ${ticket.ticketId}.`);
    } catch (error) {
      setErrorMessage(describeApiError(error, 'Review ticket failed.'));
    } finally {
      setBusyState(null);
    }
  }, [projectId, reviewBlockedReason, session.client, ticket]);

  const loadReviewPackage = useCallback(async () => {
    if (!projectId || !ticket || !run || packageBlockedReason) {
      return;
    }

    setBusyState('loadPackage');
    setErrorMessage(null);

    try {
      const response = await session.client.getRunReviewPackage(projectId, ticket.ticketId, run.runId);
      setReviewPackage(response);
      setStatusMessage(`Loaded review package for run ${response.runId}.`);
    } catch (error) {
      setErrorMessage(describeApiError(error, 'Load review package failed.'));
    } finally {
      setBusyState(null);
    }
  }, [packageBlockedReason, projectId, run, session.client, ticket]);

  const startRun = useCallback(async () => {
    if (!projectId || !ticket || !review || runBlockedReason) {
      return;
    }

    setBusyState('startRun');
    setErrorMessage(null);
    setReviewPackage(null);

    try {
      const response = await session.client.startDisposableCodeRun(projectId, ticket.ticketId, {
        reviewId: review.reviewId
      });
      setRun(response);
      setStatusMessage(`Sandbox code run ${response.runId} ended ${response.state}.`);

      try {
        const packageResponse = await session.client.getRunReviewPackage(projectId, ticket.ticketId, response.runId);
        setReviewPackage(packageResponse);
        setStatusMessage(`Sandbox code run ${response.runId} ended ${response.state}; review package loaded.`);
      } catch (packageError) {
        setErrorMessage(describeApiError(packageError, 'Run finished, but the review package is not available yet.'));
      }
    } catch (error) {
      setErrorMessage(describeApiError(error, 'Start sandbox code run failed.'));
    } finally {
      setBusyState(null);
    }
  }, [projectId, review, runBlockedReason, session.client, ticket]);

  const stages = useMemo(
    () => createStages(document, ticket, review, run, reviewPackage, busyState, errorMessage, accessBlockedReason),
    [accessBlockedReason, busyState, document, errorMessage, review, reviewPackage, run, ticket]
  );
  const currentStage = useMemo(() => stages.find((stage) => stage.status === 'running' || stage.status === 'ready' || stage.status === 'failed' || stage.status === 'blocked') ?? stages.at(-1), [stages]);

  const commands: WorkspaceCommand[] = useMemo(
    () => [
      {
        id: 'chatBuild.saveDiscussion',
        label: 'Save Discussion',
        intent: 'primary',
        onExecute: saveDiscussion,
        disabled: isBusy || Boolean(discussionBlockedReason),
        disabledReason: discussionBlockedReason ?? undefined,
        busy: busyState === 'saveDiscussion',
        testId: 'chat-build.command.header.saveDiscussion'
      },
      {
        id: 'chatBuild.createTicket',
        label: 'Create Ticket',
        intent: 'secondary',
        onExecute: createTicket,
        disabled: isBusy || Boolean(ticketBlockedReason),
        disabledReason: ticketBlockedReason ?? undefined,
        busy: busyState === 'createTicket',
        testId: 'chat-build.command.header.createTicket'
      },
      {
        id: 'chatBuild.reviewTicket',
        label: 'Review Ticket',
        intent: 'secondary',
        onExecute: reviewTicket,
        disabled: isBusy || Boolean(reviewBlockedReason),
        disabledReason: reviewBlockedReason ?? undefined,
        busy: busyState === 'reviewTicket',
        testId: 'chat-build.command.header.reviewTicket'
      },
      {
        id: 'chatBuild.startRun',
        label: 'Start Sandbox Run',
        intent: 'secondary',
        onExecute: startRun,
        disabled: isBusy || Boolean(runBlockedReason),
        disabledReason: runBlockedReason ?? undefined,
        busy: busyState === 'startRun',
        testId: 'chat-build.command.header.startRun'
      },
      {
        id: 'chatBuild.reset',
        label: 'Reset',
        intent: 'ghost',
        onExecute: resetAll,
        disabled: isBusy,
        testId: 'chat-build.command.header.reset'
      }
    ],
    [
      busyState,
      createTicket,
      discussionBlockedReason,
      isBusy,
      resetAll,
      reviewBlockedReason,
      reviewTicket,
      runBlockedReason,
      saveDiscussion,
      startRun,
      ticketBlockedReason
    ]
  );

  const routeSummary: WorkspaceSummaryChip[] = useMemo(
    () => [
      { label: project.selectedProjectName ?? (projectId ? `Project ${projectId}` : 'Project required'), testId: 'build.summary.project' },
      { label: currentStage ? `${currentStage.label}: ${currentStage.detail}` : 'Flow ready', testId: 'build.summary.stage' }
    ],
    [currentStage, project.selectedProjectName, projectId]
  );

  useEffect(() => {
    onRouteReady?.({
      workspaceCommands: commands,
      workspaceBlockReason: accessBlockedReason,
      workspaceSummaryChips: routeSummary,
      blockReasonTestId: accessBlockedReason ? 'build.blockedReason' : undefined
    });
  }, [accessBlockedReason, commands, onRouteReady, routeSummary]);

  return (
    <main className="chat-build-workspace" data-testid="build.workspace" aria-label={route.label}>
      <Surface className="chat-build-hero" testId="chat-build.status">
        <div className="chat-build-hero__copy">
          <p className="eyebrow">Build workflow</p>
          <h2>Build</h2>
          <p>Turn project discussion into a ticket, backend review, sandbox code run, and human approval package.</p>
        </div>
        <div className="chat-build-hero__meta">
          <StatusBadge status={errorMessage ? 'danger' : accessBlockedReason ? 'warning' : 'info'}>{errorMessage ? 'Failed' : currentStage?.label ?? 'Ready'}</StatusBadge>
          <span>{errorMessage ?? statusMessage}</span>
          <code>{project.selectedProjectName ?? (projectId ? `Project ${projectId}` : 'No project selected')}</code>
        </div>
        <FlowStageRail stages={stages} />
        <p className="chat-build-safety-note">Review-only path. The UI never sends commands, paths, source roots, or apply instructions.</p>
      </Surface>
      <div className="chat-build-grid">
        <div className="chat-build-grid__composer">
          <DiscussionComposer
            title={title}
            content={content}
            isBusy={busyState === 'saveDiscussion'}
            disabledReason={discussionBlockedReason}
            onTitleChange={setTitle}
            onContentChange={setContent}
            onSave={saveDiscussion}
          />
          <DiscussionDocumentCard document={document} />
        </div>
        <div className="chat-build-grid__flow">
          <GeneratedTicketPanel
            ticket={ticket}
            ticketDetail={ticketDetail}
            isBusy={busyState === 'createTicket'}
            disabledReason={ticketBlockedReason}
            onCreateTicket={createTicket}
          />
          <TicketReviewPanel
            review={review}
            isBusy={busyState === 'reviewTicket'}
            disabledReason={reviewBlockedReason}
            onReviewTicket={reviewTicket}
          />
          <DisposableRunPanel
            run={run}
            events={reviewPackage?.events ?? []}
            isBusy={busyState === 'startRun' || busyState === 'loadPackage'}
            disabledReason={runBlockedReason}
            onStartRun={startRun}
            onLoadPackage={loadReviewPackage}
          />
        </div>
        <div className="chat-build-grid__evidence">
          <RunReviewPackagePanel reviewPackage={reviewPackage} />
        </div>
      </div>
    </main>
  );
}

function createStages(
  document: SaveDiscussionResponse | null,
  ticket: CreateTicketFromDocumentResponse | null,
  review: RunTicketReviewResponse | null,
  run: StartDisposableCodeRunResponse | null,
  reviewPackage: RunReviewPackage | null,
  busyState: ChatBuildBusyState,
  errorMessage: string | null,
  accessBlockedReason: string | null
): FlowStageItem[] {
  const runningStage = busyState ? busyStateToStageId(busyState) : null;
  const failedStage = errorMessage ? runningStage ?? firstIncompleteStage(document, ticket, review, run, reviewPackage) : null;
  const blockedStage = accessBlockedReason ? firstIncompleteStage(document, ticket, review, run, reviewPackage) : null;

  return [
    createStage('discussion', 'Discussion', Boolean(document), true, runningStage, failedStage, blockedStage),
    createStage('document', 'Document', Boolean(document), Boolean(document), runningStage, failedStage, blockedStage),
    createStage('ticket', 'Ticket', Boolean(ticket), Boolean(document), runningStage, failedStage, blockedStage),
    createStage('review', 'Review', Boolean(review), Boolean(ticket), runningStage, failedStage, blockedStage),
    createStage('run', 'Sandbox Run', Boolean(run), Boolean(review), runningStage, failedStage, blockedStage),
    createStage('package', 'Review Package', Boolean(reviewPackage), Boolean(run), runningStage, failedStage, blockedStage)
  ];
}

function createStage(
  id: string,
  label: string,
  done: boolean,
  ready: boolean,
  runningStage: string | null,
  failedStage: string | null,
  blockedStage: string | null
): FlowStageItem {
  if (failedStage === id) {
    return { id, label, status: 'failed', detail: 'Failed' };
  }

  if (runningStage === id) {
    return { id, label, status: 'running', detail: 'Running' };
  }

  if (blockedStage === id) {
    return { id, label, status: 'blocked', detail: 'Blocked' };
  }

  if (done) {
    return { id, label, status: 'done', detail: 'Done' };
  }

  if (ready) {
    return { id, label, status: 'ready', detail: 'Ready' };
  }

  return { id, label, status: 'not-started', detail: 'Not started' };
}

function busyStateToStageId(busyState: ChatBuildBusyState) {
  switch (busyState) {
    case 'saveDiscussion':
      return 'discussion';
    case 'createTicket':
      return 'ticket';
    case 'reviewTicket':
      return 'review';
    case 'startRun':
      return 'run';
    case 'loadPackage':
      return 'package';
    default:
      return null;
  }
}

function firstIncompleteStage(
  document: SaveDiscussionResponse | null,
  ticket: CreateTicketFromDocumentResponse | null,
  review: RunTicketReviewResponse | null,
  run: StartDisposableCodeRunResponse | null,
  reviewPackage: RunReviewPackage | null
) {
  if (!document) {
    return 'discussion';
  }
  if (!ticket) {
    return 'ticket';
  }
  if (!review) {
    return 'review';
  }
  if (!run) {
    return 'run';
  }
  if (!reviewPackage) {
    return 'package';
  }

  return 'package';
}

function getAccessBlockedReason(tokenConfigured: boolean, projectId: number | null, accessStatus: string) {
  if (!tokenConfigured) {
    return 'Authentication is required.';
  }

  if (!projectId) {
    return 'Select a project first.';
  }

  if (accessStatus === 'apiOffline') {
    return 'IronDev.Api is offline.';
  }

  if (accessStatus === 'authInvalid') {
    return 'Authentication is invalid.';
  }

  return null;
}

function getDiscussionBlockedReason(title: string, content: string) {
  if (!title.trim()) {
    return 'Discussion title is required.';
  }

  if (!content.trim()) {
    return 'Discussion text is required.';
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
