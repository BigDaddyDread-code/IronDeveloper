import type { RunEventSummary } from '../../api/types';

export function RunEventTimeline({
  events,
  compact = false,
  testId = 'chat-build.runEventTimeline'
}: {
  events: RunEventSummary[];
  compact?: boolean;
  testId?: string;
}) {
  return (
    <section className={`chat-build-timeline${compact ? ' chat-build-timeline--compact' : ''}`} data-testid={testId}>
      <div className="section-heading">
        <p className="eyebrow">Events</p>
        <h2>Run timeline</h2>
      </div>
      {events.length > 0 ? (
        <ol>
          {events.map((event, index) => (
            <li key={`${event.eventType}-${event.timestampUtc}-${index}`}>
              <span>{event.eventType}</span>
              <p>{event.message}</p>
              <time>{formatDate(event.timestampUtc)}</time>
            </li>
          ))}
        </ol>
      ) : (
        <p className="state-muted">No persisted run events loaded yet.</p>
      )}
    </section>
  );
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
