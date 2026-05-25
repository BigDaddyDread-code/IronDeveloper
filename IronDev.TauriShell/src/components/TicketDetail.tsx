import type {
  BuildReadinessResult,
  ProjectTicket,
  TicketDetailLoadStatus,
  TicketReadinessLoadStatus
} from '../api/types';
import { DateTimeDisplay } from '../utils/dateTimeDisplay';
import { CommandButton } from './CommandButton';
import { EmptyState } from './EmptyState';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';

interface TicketDetailProps {
  ticket: ProjectTicket | null;
  detailStatus: TicketDetailLoadStatus;
  detailMessage: string;
  readiness: BuildReadinessResult | null;
  readinessStatus: TicketReadinessLoadStatus;
  readinessMessage: string;
  onRefreshReadiness: () => void;
}

export function TicketDetail({
  ticket,
  detailStatus,
  detailMessage,
  readiness,
  readinessStatus,
  readinessMessage,
  onRefreshReadiness
}: TicketDetailProps) {
  if (detailStatus === 'loading') {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <EmptyState title="Loading selected ticket" body="IronDev is loading the selected ticket detail through the API." />
      </SurfacePanel>
    );
  }

  if (detailStatus === 'error') {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <EmptyState title="Ticket detail unavailable" body={detailMessage} action={<StatusBadge status="warning">Retry from queue</StatusBadge>} />
      </SurfacePanel>
    );
  }

  if (!ticket) {
    return (
      <SurfacePanel className="ticket-detail ticket-detail--empty" testId="ticket.detail">
        <EmptyState
          title="No ticket selected"
          body="Select a ticket from the queue to inspect the workflow detail, readiness, and evidence context."
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
          <StatusBadge status={readiness?.isReady ? 'ready' : readinessStatus === 'loaded' ? 'warning' : 'neutral'}>
            {readinessStatus === 'loaded' ? readinessLabel(readiness?.status) : 'Readiness pending'}
          </StatusBadge>
        </div>
      </div>

      <section className="workflow-section workflow-section--wide" data-testid="ticket.detail.brief">
        <div className="workflow-section__header">
          <h3>Brief</h3>
          <StatusBadge status="info">API-backed</StatusBadge>
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

      <div className="detail-grid detail-grid--workflow">
        <section className="workflow-section" data-testid="ticket.detail.plan">
          <div className="workflow-section__header">
            <h3>Plan</h3>
            <StatusBadge status={ticket.technicalNotes || ticket.content ? 'ready' : 'neutral'}>
              {ticket.technicalNotes || ticket.content ? 'Captured' : 'Unavailable'}
            </StatusBadge>
          </div>
          <p>{ticket.technicalNotes ?? ticket.content ?? 'Implementation plan data is not exposed for this ticket yet.'}</p>
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
            disabled={readinessStatus === 'loading'}
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
