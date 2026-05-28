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
  const planner = getContributions(result, ['Plan', 'Planner']);
  const proposal = getContributions(result, ['Proposal', 'Builder']);
  const validation = getContributions(result, ['Validation', 'Tester']);
  const governance = getContributions(result, ['Governance', 'Critic']);

  return (
    <Surface className="chat-build-panel chat-build-panel--review" testId="chat-build.ticketReview">
      <div className="section-heading">
        <p className="eyebrow">Review</p>
        <h2>Plan and guardrails</h2>
      </div>
      {result ? (
        <div className="chat-build-review">
          <div className="metadata-stack">
            <MetadataRow label="Review" value={result.reviewId} />
            <MetadataRow label="Mode" value={result.scenarioId === 'model.assisted' ? 'Model assisted' : 'Deterministic review'} />
            <MetadataRow
              label="Decision"
              value={<StatusBadge status={result.decision.proceed ? 'ready' : 'warning'}>{result.decision.proceed ? 'Proceed' : 'Blocked'}</StatusBadge>}
            />
            <MetadataRow label="Next step" value={result.decision.recommendedNextStep} />
          </div>
          <ReviewSection title="Plan" contributions={planner} fallback="No planner contribution returned." />
          <ReviewSection title="Proposal" contributions={proposal} fallback="No builder contribution returned." />
          <ReviewSection title="Validation" contributions={validation} fallback="No tester contribution returned." />
          <ReviewSection title="Governance" contributions={governance} fallback="No critic contribution returned." />
          <ListItems title="Guardrails" items={result.decision.guardrails} tone="guardrail" />
        </div>
      ) : (
        <p className="state-muted">Review the generated ticket before starting a sandbox code run.</p>
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

function ReviewSection({
  title,
  contributions,
  fallback
}: {
  title: string;
  contributions: NonNullable<RunTicketReviewResponse['result']>['contributions'];
  fallback: string;
}) {
  return (
    <section className="chat-build-review-section">
      <p className="section-subtitle">{title}</p>
      {contributions.length > 0 ? (
        contributions.map((contribution) => (
          <article key={contribution.role} className="chat-build-contribution">
            <h3>{contribution.role}</h3>
            <p>{contribution.summary}</p>
            <ListItems title="Concerns" items={contribution.concerns} />
            <ListItems title="Recommendations" items={contribution.recommendations} />
          </article>
        ))
      ) : (
        <p className="state-muted">{fallback}</p>
      )}
    </section>
  );
}

function ListItems({ title, items, tone }: { title: string; items: string[]; tone?: 'guardrail' }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div className={`chat-build-sublist${tone ? ` chat-build-sublist--${tone}` : ''}`}>
      <p className="section-subtitle">{title}</p>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

function getContributions(result: RunTicketReviewResponse['result'] | null, roles: string[]) {
  if (!result) {
    return [];
  }

  const roleSet = new Set(roles.map((role) => role.toLowerCase()));
  return result.contributions.filter((contribution) => roleSet.has(contribution.role.toLowerCase()));
}
