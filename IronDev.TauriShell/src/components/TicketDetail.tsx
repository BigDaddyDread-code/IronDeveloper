import type {
  BuildReadinessResult,
  ProjectImplementationPlan,
  ProjectTicket,
  TicketEvidenceLoadStatus,
  TicketEvidenceSummary,
  TicketDetailLoadStatus,
  TicketPlanStatus,
  TicketReadinessLoadStatus,
  TicketSaveStatus
} from '../api/types';
import { DateTimeDisplay } from '../utils/dateTimeDisplay';
import { CommandButton } from './CommandButton';
import { EmptyState } from './EmptyState';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';
import { TicketEditForm, type TicketEditDraft } from './TicketEditForm';
import { ErrorState } from '../design-system/state/ErrorState';
import { LoadingState } from '../design-system/state/LoadingState';

interface TicketDetailProps {
  ticket: ProjectTicket | null;
  detailStatus: TicketDetailLoadStatus;
  detailMessage: string;
  readiness: BuildReadinessResult | null;
  readinessStatus: TicketReadinessLoadStatus;
  readinessMessage: string;
  evidenceSummary: TicketEvidenceSummary | null;
  evidenceStatus: TicketEvidenceLoadStatus;
  evidenceMessage: string;
  implementationPlan: ProjectImplementationPlan | null;
  planStatus: TicketPlanStatus;
  planMessage: string;
  isEditing: boolean;
  editDraft: TicketEditDraft;
  saveStatus: TicketSaveStatus;
  saveMessage: string;
  isEditDirty: boolean;
  editValidationMessage: string | null;
  editBlockedReason: string | null;
  onEdit: () => void;
  onEditDraftChange: (draft: TicketEditDraft) => void;
  onSave: () => void;
  onCancelEdit: () => void;
  onRefreshPlan: () => void;
  onRefreshReadiness: () => void;
  onRefreshEvidence: () => void;
  onReviewLatestRun: () => void;
  onOpenPromotionReview: () => void;
}

export function TicketDetail({
  ticket,
  detailStatus,
  detailMessage,
  readiness,
  readinessStatus,
  readinessMessage,
  evidenceSummary,
  evidenceStatus,
  evidenceMessage,
  implementationPlan,
  planStatus,
  planMessage,
  isEditing,
  editDraft,
  saveStatus,
  saveMessage,
  isEditDirty,
  editValidationMessage,
  editBlockedReason,
  onEdit,
  onEditDraftChange,
  onSave,
  onCancelEdit,
  onRefreshPlan,
  onRefreshReadiness,
  onRefreshEvidence,
  onReviewLatestRun,
  onOpenPromotionReview
}: TicketDetailProps) {
  if (detailStatus === 'loading') {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <LoadingState title="Loading selected ticket" body="IronDev is loading the selected ticket detail through the API." />
      </SurfacePanel>
    );
  }

  if (detailStatus === 'error') {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <ErrorState title="Ticket detail unavailable" body={detailMessage} action={<StatusBadge status="warning">Retry from tickets</StatusBadge>} />
      </SurfacePanel>
    );
  }

  if (!ticket) {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <EmptyState
          title="No ticket selected"
          body="Select a ticket to inspect the workflow detail, build readiness, and context linked to this ticket."
        />
      </SurfacePanel>
    );
  }

  const acceptanceCriteria = splitList(ticket.acceptanceCriteria);
  const affectedFiles = splitList(ticket.linkedFilePaths);
  const affectedSymbols = splitList(ticket.linkedSymbols);
  const createdMetadata = ticket.createdDate ? (
    <span title={DateTimeDisplay.toUtcTooltip(ticket.createdDate)}>
      {DateTimeDisplay.toLocalDisplay(ticket.createdDate)} - {DateTimeDisplay.toUtcMetadata(ticket.createdDate)}
    </span>
  ) : (
    'CreatedUtc unavailable'
  );

  if (isEditing) {
    return (
      <SurfacePanel className="ticket-detail" testId="ticket.detail">
        <TicketEditForm
          draft={editDraft}
          status={saveStatus}
          message={saveMessage}
          isDirty={isEditDirty}
          validationMessage={editValidationMessage}
          blockedReason={editBlockedReason}
          onChange={onEditDraftChange}
          onSave={onSave}
          onCancel={onCancelEdit}
        />
      </SurfacePanel>
    );
  }

  return (
    <SurfacePanel className="ticket-detail" testId="ticket.detail">
      <div className="ticket-detail__header" data-testid="ticket.detail.header">
        <div className="ticket-detail__title">
          <p className="eyebrow">Selected ticket</p>
          <h2>{ticket.title ?? `Ticket ${ticket.id}`}</h2>
          <p>{ticket.summary ?? ticket.problem ?? 'No brief captured yet.'}</p>
          <div className="ticket-detail__meta">
            <MetadataRow label="Created UTC" value={createdMetadata} />
            <MetadataRow label="Ticket id" value={ticket.id ? `#${ticket.id}` : 'Unavailable'} />
          </div>
        </div>
        <div className="ticket-detail__badges">
          <StatusBadge status="neutral">{ticket.status ?? 'Draft'}</StatusBadge>
          <StatusBadge status="info">{ticket.priority ?? 'Medium'}</StatusBadge>
          <StatusBadge status="neutral">{ticket.ticketType ?? 'Work item'}</StatusBadge>
          {saveStatus === 'saved' ? <StatusBadge status="ready" data-testid="ticket.edit.success">{saveMessage}</StatusBadge> : null}
          {saveStatus === 'error' ? <StatusBadge status="warning" data-testid="ticket.edit.error">{saveMessage}</StatusBadge> : null}
          <StatusBadge status={readiness?.isReady ? 'ready' : readinessStatus === 'loaded' ? 'warning' : 'neutral'}>
            {readinessStatus === 'loaded' ? readinessLabel(readiness?.status) : 'Readiness pending'}
          </StatusBadge>
          <CommandButton
            type="button"
            variant="secondary"
            testId="ticket.command.edit"
            disabled={Boolean(editBlockedReason)}
            title={editBlockedReason ?? undefined}
            onClick={onEdit}
          >
            Edit
          </CommandButton>
        </div>
      </div>

      <section className="workflow-section workflow-section--wide" data-testid="ticket.detail.brief">
        <div className="workflow-section__header">
          <h3>Brief</h3>
          <StatusBadge status="info">Project data</StatusBadge>
        </div>
        <MetadataRow label="Summary" value={ticket.summary ?? 'No summary captured.'} />
        <MetadataRow label="Problem" value={ticket.problem ?? 'No problem statement captured.'} />
        <MetadataRow label="Proposed change" value={ticket.content ?? ticket.technicalNotes ?? 'No proposed change captured.'} />
        <div data-testid="ticket.detail.acceptanceCriteria">
          <h4>Acceptance criteria</h4>
          {acceptanceCriteria.length > 0 ? (
            <ul className="detail-list">
              {acceptanceCriteria.map((criterion) => (
                <li key={criterion}>{criterion}</li>
              ))}
            </ul>
          ) : (
            <p className="state-muted">Acceptance criteria unavailable.</p>
          )}
        </div>
      </section>

      <section className="workflow-section workflow-section--wide" data-testid="ticket.detail.executionEvidence">
        <div className="workflow-section__header">
          <h3>Evidence</h3>
          <StatusBadge status={evidenceSummary ? (evidenceSummary.hasBlockingWarnings ? 'warning' : 'ready') : 'neutral'}>
            {evidenceStatus === 'loaded'
              ? evidenceSummary?.hasBlockingWarnings
                ? 'Requires follow-up'
                : 'Evidence available'
              : evidenceStatus === 'loading'
                ? 'Loading'
                : 'Not loaded'}
          </StatusBadge>
        </div>
        {evidenceStatus === 'loading' ? (
          <p>Loading evidence...</p>
        ) : !evidenceSummary ? (
          <p data-testid="ticket.evidence.empty">{evidenceMessage}</p>
        ) : evidenceSummary.latestRun || evidenceSummary.latestPromotionPackage ? (
          <>
            {evidenceSummary.latestRun ? (
              <div data-testid="ticket.evidence.latestRun" className="detail-panel">
                <p className="section-subtitle">Linked run</p>
                <MetadataRow label="Run ID" value={evidenceSummary.latestRun.runId} />
                <MetadataRow label="Status" value={evidenceSummary.latestRun.status} />
                <MetadataRow label="Recommendation" value={evidenceSummary.latestRun.recommendation ?? 'No recommendation exposed.'} />
                <MetadataRow label="Context trace" value={evidenceSummary.latestRun.traceId ?? 'No context trace exposed.'} />
                <CommandButton type="button" variant="secondary" onClick={onReviewLatestRun}>
                  Review run
                </CommandButton>
              </div>
            ) : (
              <p data-testid="ticket.evidence.empty">{evidenceMessage}</p>
            )}
            {evidenceSummary.latestPromotionPackage ? (
              <div data-testid="ticket.evidence.latestPromotionPackage" className="detail-panel">
                <p className="section-subtitle">Latest promotion package</p>
                <MetadataRow label="Package" value={evidenceSummary.latestPromotionPackage.packageId ?? 'Unavailable'} />
                <MetadataRow
                  label="Files to promote / blocked"
                  value={`${evidenceSummary.latestPromotionPackage.filesToPromoteCount ?? 0} / ${evidenceSummary.latestPromotionPackage.filesBlockedCount ?? 0}`}
                />
                <MetadataRow label="Approval state" value={evidenceSummary.latestPromotionPackage.approvalState ?? 'Unavailable'} />
                <MetadataRow label="Recommendation" value={evidenceSummary.latestPromotionPackage.recommendation ?? 'No recommendation exposed.'} />
                <CommandButton type="button" variant="secondary" onClick={onOpenPromotionReview}>
                  Open promotion review
                </CommandButton>
              </div>
            ) : null}
            <MetadataRow label="Linked documents" value={`${evidenceSummary.linkedDocumentCount}`} />
            <MetadataRow label="Context traces" value={`${evidenceSummary.linkedTraceCount}`} />
            <MetadataRow label="Linked decisions" value={`${evidenceSummary.linkedDecisionCount}`} />
            <MetadataRow label="Linked runs" value={`${evidenceSummary.linkedRunCount}`} />
            <div className="workflow-section__meta" data-testid="ticket.evidence.blockedActions">
              <p>Blocked actions</p>
              {evidenceSummary.blockedActions.length > 0 ? (
                <ul className="detail-list">
                  {evidenceSummary.blockedActions.map((action) => (
                    <li key={action}>{action}</li>
                  ))}
                </ul>
              ) : (
                <p className="state-muted">No blocked actions.</p>
              )}
            </div>
            <MetadataRow
              label="Next safe action"
              value={evidenceSummary.nextSafeAction ?? 'Refresh readiness'}
            />
          </>
        ) : (
          <div data-testid="ticket.evidence.empty">
            <p>No build evidence yet. Start a sandbox run to produce context trace, build output, and review data.</p>
            <p>{evidenceMessage}</p>
          </div>
        )}

        <CommandButton type="button" variant="subtle" onClick={onRefreshEvidence} testId="ticket.command.refreshEvidence">
          Refresh evidence
        </CommandButton>
      </section>

      <div className="detail-grid detail-grid--workflow">
        <section className="workflow-section" data-testid="ticket.detail.plan">
          <div className="workflow-section__header">
            <h3>Plan</h3>
            <StatusBadge status={implementationPlan ? 'ready' : planStatus === 'error' ? 'warning' : 'neutral'}>
              {planStatusLabel(planStatus, implementationPlan)}
            </StatusBadge>
          </div>
          {implementationPlan ? (
            <div className="readiness-block">
              <MetadataRow label="Goal" value={implementationPlan.goal ?? 'No goal exposed.'} />
              <MetadataRow label="Scope" value={implementationPlan.scope ?? 'No scope exposed.'} />
              <MetadataRow label="Steps" value={implementationPlan.proposedSteps ?? 'No steps exposed.'} />
              <MetadataRow label="Risks" value={implementationPlan.risksNotes ?? 'No plan risks exposed.'} />
              {implementationPlan.updatedDate || implementationPlan.createdDate ? (
                <MetadataRow
                  label={implementationPlan.updatedDate ? 'Updated UTC' : 'Created UTC'}
                  value={DateTimeDisplay.toUtcMetadata(implementationPlan.updatedDate ?? implementationPlan.createdDate)}
                />
              ) : null}
            </div>
          ) : (
            <p>{planMessage}</p>
          )}
          <CommandButton
            type="button"
            variant="secondary"
            testId="ticket.command.generatePlan"
            disabled={planStatus === 'loading' || Boolean(editBlockedReason)}
            title={editBlockedReason ?? undefined}
            onClick={onRefreshPlan}
          >
            {planStatus === 'loading' ? 'Refreshing plan' : 'Refresh plan'}
          </CommandButton>
        </section>

        <section className="workflow-section" data-testid="ticket.detail.context">
          <div className="workflow-section__header">
            <h3>Context</h3>
            <StatusBadge status={ticket.contextSummary ? 'ready' : 'neutral'}>
              {ticket.contextSummary ? 'Context linked' : 'Missing context'}
            </StatusBadge>
          </div>
          <p>{ticket.contextSummary ?? 'Context summary is unavailable from the current ticket API payload.'}</p>
          <MetadataRow label="Affected files" value={affectedFiles.length > 0 ? `${affectedFiles.length}` : 'None exposed'} />
          <MetadataRow label="Affected symbols" value={affectedSymbols.length > 0 ? `${affectedSymbols.length}` : 'None exposed'} />
        </section>

        <section className="workflow-section" data-testid="ticket.detail.tests">
          <div className="workflow-section__header">
            <h3>Tests</h3>
            <StatusBadge status={hasTestData(ticket) ? 'ready' : 'neutral'}>
              {hasTestData(ticket) ? 'Captured' : 'Unavailable'}
            </StatusBadge>
          </div>
          <MetadataRow label="Unit" value={ticket.unitTests ?? 'No unit test plan exposed.'} />
          <MetadataRow label="Integration" value={ticket.integrationTests ?? 'No integration test plan exposed.'} />
          <MetadataRow label="Manual" value={ticket.manualTests ?? 'No manual test plan exposed.'} />
          <MetadataRow label="Regression" value={ticket.regressionTests ?? 'No regression test plan exposed.'} />
        </section>

        <section className="workflow-section" data-testid="ticket.detail.build">
          <div className="workflow-section__header">
            <h3>Build</h3>
            <StatusBadge status={readinessStatusTone(readinessStatus, readiness)}>{readinessStatusLabel(readinessStatus, readiness)}</StatusBadge>
          </div>
          <div data-testid="ticket.detail.readiness">
            <p>{readinessMessage}</p>
            {readiness ? (
              <div className="readiness-block">
                <MetadataRow label="Status" value={readinessLabel(readiness.status)} />
                <MetadataRow label="Ready" value={readiness.isReady ? 'Yes' : 'No'} />
                {readiness.warnings && readiness.warnings.length > 0 ? (
                  <DetailList title="Warnings" items={readiness.warnings} />
                ) : null}
                {readiness.blockingIssues && readiness.blockingIssues.length > 0 ? (
                  <DetailList title="Blocking issues" items={readiness.blockingIssues} />
                ) : null}
              </div>
            ) : null}
          </div>
          <CommandButton
            type="button"
            variant="primary"
            testId="ticket.command.refreshReadiness"
            disabled={readinessStatus === 'loading' || Boolean(editBlockedReason)}
            title={editBlockedReason ?? undefined}
            onClick={onRefreshReadiness}
          >
            {readinessStatus === 'loading' ? 'Checking readiness' : 'Refresh readiness'}
          </CommandButton>
        </section>
      </div>
    </SurfacePanel>
  );
}

function DetailList({ title, items }: { title: string; items: string[] }) {
  return (
    <div>
      <h4>{title}</h4>
      <ul className="detail-list">
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

function splitList(value?: string | null) {
  if (!value) {
    return [];
  }

  return value
    .split(/\r?\n|;|\|/)
    .map((item) => item.replace(/^[-*]\s*/, '').trim())
    .filter(Boolean);
}

function hasTestData(ticket: ProjectTicket) {
  return Boolean(ticket.unitTests || ticket.integrationTests || ticket.manualTests || ticket.regressionTests);
}

function readinessLabel(status?: BuildReadinessResult['status']) {
  switch (status) {
    case 0:
      return 'Ready to build';
    case 1:
      return 'Needs project profile';
    case 2:
      return 'Needs reindex';
    case 3:
      return 'Needs decision';
    case 4:
      return 'Blocked by decision';
    case 5:
      return 'Blocked by conflict';
    case 6:
      return 'Needs clarification';
    case 7:
      return 'Readiness error';
    default:
      return 'Readiness unavailable';
  }
}

function readinessStatusLabel(status: TicketReadinessLoadStatus, readiness: BuildReadinessResult | null) {
  switch (status) {
    case 'loading':
      return 'Checking';
    case 'loaded':
      return readiness?.isReady ? 'Ready' : 'Needs attention';
    case 'unavailable':
      return 'Unavailable';
    case 'error':
      return 'Error';
    default:
      return 'Not checked';
  }
}

function readinessStatusTone(status: TicketReadinessLoadStatus, readiness: BuildReadinessResult | null) {
  if (status === 'loading') {
    return 'loading';
  }

  if (status === 'loaded') {
    return readiness?.isReady ? 'ready' : 'warning';
  }

  if (status === 'error') {
    return 'danger';
  }

  return 'neutral';
}

function planStatusLabel(status: TicketPlanStatus, implementationPlan: ProjectImplementationPlan | null) {
  if (status === 'loading') {
    return 'Refreshing';
  }

  if (implementationPlan) {
    return 'Plan loaded';
  }

  if (status === 'error') {
    return 'Error';
  }

  if (status === 'unavailable') {
    return 'Unavailable';
  }

  return 'Not loaded';
}
