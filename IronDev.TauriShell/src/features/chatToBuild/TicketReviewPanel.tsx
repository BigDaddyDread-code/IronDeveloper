import { CommandButton } from '../../components/CommandButton';
import { MetadataRow } from '../../components/MetadataRow';
import { Surface } from '../../design-system/Surface';
import { StatusBadge } from '../../components/StatusBadge';
import type { RunTicketReviewResponse } from '../../api/types';

interface TicketReviewPanelProps {
  review: RunTicketReviewResponse | null;
  isBusy: boolean;
  disabledReason: string | null;
  onReviewTicket: () => void;
}

export function TicketReviewPanel({ review, isBusy, disabledReason, onReviewTicket }: TicketReviewPanelProps) {
  const result = review?.result ?? null;

  return (
    <Surface className="chat-build-panel" testId="chat-build.ticketReview">
      <div className="section-heading">
        <p className="eyebrow">Review</p>
        <h2>Ticket review</h2>
      </div>
      {result ? (
        <div className="chat-build-review">
          <div className="metadata-stack">
            <MetadataRow label="Review" value={result.reviewId} />
            <MetadataRow label="Scenario" value={result.scenarioId} />
            <MetadataRow
              label="Decision"
              value={<StatusBadge status={result.decision.proceed ? 'ready' : 'warning'}>{result.decision.proceed ? 'Proceed' : 'Blocked'}</StatusBadge>}
            />
            <MetadataRow label="Next step" value={result.decision.recommendedNextStep} />
          </div>
          <div className="chat-build-list">
            {result.contributions.map((contribution) => (
              <article key={contribution.role} className="chat-build-contribution">
                <h3>{contribution.role}</h3>
                <p>{contribution.summary}</p>
                <ListItems title="Concerns" items={contribution.concerns} />
                <ListItems title="Recommendations" items={contribution.recommendations} />
              </article>
            ))}
          </div>
          <ListItems title="Guardrails" items={result.decision.guardrails} />
        </div>
      ) : (
        <p className="state-muted">Review the generated ticket before starting a disposable code run.</p>
      )}
      <div className="chat-build-actions">
        <CommandButton
          type="button"
          variant="secondary"
          onClick={onReviewTicket}
          disabled={isBusy || Boolean(disabledReason)}
          testId="chat-build.command.reviewTicket"
        >
          {isBusy ? 'Reviewing...' : 'Review Ticket'}
        </CommandButton>
        {disabledReason ? <p className="state-muted">{disabledReason}</p> : null}
      </div>
    </Surface>
  );
}

function ListItems({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div className="chat-build-sublist">
      <p className="section-subtitle">{title}</p>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}
