import type { TicketRunReview, TicketEvidenceLoadStatus } from '../api/types';
import { DateTimeDisplay } from '../utils/dateTimeDisplay';
import { CommandButton } from './CommandButton';
import { EmptyState } from './EmptyState';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';

interface TicketRunReviewPanelProps {
  review: TicketRunReview | null;
  status: TicketEvidenceLoadStatus;
  message: string;
  onRefresh: () => void;
  onDismiss: () => void;
}

export function TicketRunReviewPanel({
  review,
  status,
  message,
  onRefresh,
  onDismiss
}: TicketRunReviewPanelProps) {
  return (
    <SurfacePanel className="ticket-run-review" testId="ticket.runReview">
      <div className="workflow-section__header">
        <div>
          <p className="eyebrow">Run review</p>
          <h3>Latest disposable run</h3>
        </div>
        <div className="ticket-detail__badges">
          <CommandButton type="button" variant="secondary" onClick={onRefresh} disabled={status === 'loading'} testId="ticket.runReview.refresh">
            {status === 'loading' ? 'Loading run' : 'Refresh run'}
          </CommandButton>
          <CommandButton type="button" variant="subtle" onClick={onDismiss} testId="ticket.runReview.dismiss">
            Close
          </CommandButton>
        </div>
      </div>

      {status === 'loading' ? (
        <EmptyState title="Loading run review" body="IronDev.Api is loading ticket-scoped run evidence." />
      ) : status === 'error' || status === 'unavailable' ? (
        <EmptyState title="Run review unavailable" body={message} action={<StatusBadge status="warning">No data fabricated</StatusBadge>} />
      ) : !review ? (
        <EmptyState title="No run selected" body="Review Latest Run opens after execution evidence links a real run to this ticket." />
      ) : (
        <div className="ticket-run-review__content">
          <div className="ticket-run-review__summary" data-testid="ticket.runReview.summary">
            <StatusBadge status={badgeStatus(review.status)} data-testid="ticket.runReview.status">
              {review.status || 'Unknown'}
            </StatusBadge>
            {review.isDisposableRun ? <StatusBadge status="info" data-testid="ticket.runReview.disposable">Disposable run</StatusBadge> : null}
            <MetadataRow label="Run ID" value={review.runId} />
            <MetadataRow label="Ticket" value={`#${review.ticketId} - ${review.ticketTitle}`} />
            <MetadataRow label="Started UTC" value={formatDate(review.startedUtc)} />
            <MetadataRow label="Completed UTC" value={formatDate(review.completedUtc)} />
            <MetadataRow label="Evidence" value={review.evidenceSummary || 'No evidence summary available.'} />
            <MetadataRow label="Output" value={review.outputSummary || 'No output summary available.'} />
            {review.failureReason ? <MetadataRow label="Failure reason" value={review.failureReason} /> : null}
          </div>

          <section className="workflow-section" data-testid="ticket.runReview.links">
            <div className="workflow-section__header">
              <h4>Trace and report links</h4>
            </div>
            <MetadataRow label="Trace ID" value={review.traceId ?? 'Trace id unavailable'} />
            <MetadataRow label="Trace" value={review.tracePath ?? 'Trace link unavailable'} />
            <MetadataRow label="Report" value={review.reportPath ?? 'Report link unavailable'} />
            <MetadataRow label="Log" value={review.logPath ?? 'Log link unavailable'} />
          </section>

          <section className="workflow-section" data-testid="ticket.runReview.evidence">
            <div className="workflow-section__header">
              <h4>Evidence used</h4>
              <StatusBadge status="neutral">{review.evidence.length} item(s)</StatusBadge>
            </div>
            {review.evidence.length === 0 ? (
              <p>No evidence files have been attached to this run yet.</p>
            ) : (
              <ul className="run-reports-evidence-list">
                {review.evidence.map((item) => (
                  <li key={`${item.type}-${item.path}`}>
                    <strong>{item.type}</strong>
                    <span>{item.summary || item.path}</span>
                    <code>{item.path}</code>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="workflow-section" data-testid="ticket.runReview.events">
            <div className="workflow-section__header">
              <h4>Run timeline</h4>
              <StatusBadge status="neutral">{review.events.length} event(s)</StatusBadge>
            </div>
            <ul className="run-report-timeline">
              {review.events.map((event, index) => (
                <li key={event.eventId ?? `${event.eventType}-${index}`}>
                  <strong>{event.eventType ?? 'Event'}</strong>
                  <span>{event.message ?? 'No event message.'}</span>
                  <small>{formatDate(event.timestampUtc)}</small>
                </li>
              ))}
            </ul>
          </section>
        </div>
      )}
    </SurfacePanel>
  );
}

function formatDate(value?: string | null) {
  if (!value) {
    return 'Unavailable';
  }

  return `${DateTimeDisplay.toLocalDisplay(value)} - ${DateTimeDisplay.toUtcMetadata(value)}`;
}

function badgeStatus(status: string): 'neutral' | 'info' | 'ready' | 'warning' {
  if (/fail|error|blocked/i.test(status)) {
    return 'warning';
  }

  if (/approval|review/i.test(status)) {
    return 'info';
  }

  if (/complete|passed/i.test(status)) {
    return 'ready';
  }

  return 'neutral';
}
