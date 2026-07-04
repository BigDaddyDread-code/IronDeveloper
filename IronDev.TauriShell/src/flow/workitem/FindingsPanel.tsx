import { useState } from 'react';
import type {
  SkeletonCriticReviewOutcome,
  SkeletonRunCriticReviewTrace,
  SkeletonRunFindingDispositionTrace
} from '../../api/types';

// P1-1..P1-3 on screen: request an independent critic review, read its findings
// (advisory — a finding is not a veto), and record the human disposition every
// finding must carry before the gate will evaluate approval. A disposition is a
// decision about a finding; it is not approval.

interface FindingsPanelProps {
  criticOutcome: SkeletonCriticReviewOutcome | null;
  criticReviews: SkeletonRunCriticReviewTrace[];
  dispositions: SkeletonRunFindingDispositionTrace[];
  busyAction: string | null;
  onRequestCriticReview: () => void;
  onRecordDisposition: (findingId: string, disposition: string, reason: string) => void;
}

export function FindingsPanel({
  criticOutcome,
  criticReviews,
  dispositions,
  busyAction,
  onRequestCriticReview,
  onRecordDisposition
}: FindingsPanelProps) {
  const [reasonByFinding, setReasonByFinding] = useState<Record<string, string>>({});
  const [kindByFinding, setKindByFinding] = useState<Record<string, string>>({});

  const dispositionedIds = new Set(dispositions.map((disposition) => disposition.findingId));
  const allFindingIds = criticReviews.flatMap((review) => review.findingIds);
  const undispositionedIds = allFindingIds.filter((findingId) => !dispositionedIds.has(findingId));

  // Finding text is available for reviews requested this session; reloaded
  // sessions still see ids, dispositions, and the gate state from the report.
  const knownFindings = new Map((criticOutcome?.findings ?? []).map((finding) => [finding.findingId, finding]));

  return (
    <>
      <p className="fl-plabel" style={{ marginTop: 14 }}>
        Independent critic
      </p>

      {criticReviews.length === 0 && criticOutcome === null ? (
        <p className="fl-empty">
          No critic review yet. The critic pulls the package from durable evidence itself and reviews with no team memory —
          its findings are advisory, and every finding will need a human disposition before continuation.
        </p>
      ) : null}

      {criticReviews.map((review) => (
        <p key={review.reviewId} style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.review.criticVerdict">
          Review {review.reviewId}: verdict <strong>{review.verdict}</strong> · {review.findingCount} finding(s) (
          {review.blockingFindingCount} blocking) · ground truth {review.groundTruthMismatchCount} mismatch(es) in{' '}
          {review.groundTruthCheckCount} check(s)
        </p>
      ))}

      {criticOutcome !== null && !criticOutcome.succeeded ? (
        <div className="fl-error">Critic review failed and was not recorded: {criticOutcome.failureReason}</div>
      ) : null}

      <div data-testid="flow.review.findings">
        {allFindingIds.map((findingId) => {
          const finding = knownFindings.get(findingId);
          const disposition = dispositions.find((candidate) => candidate.findingId === findingId);
          return (
            <div className="fl-qbox" key={findingId}>
              <span style={{ width: '100%' }}>
                <strong style={{ fontSize: 12.5 }}>
                  {finding ? `${finding.severity}: ${finding.title}` : findingId}
                  {finding?.blocksMerge ? ' · blocking' : ''}
                </strong>
                {finding ? (
                  <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>
                    {finding.problem} Why it matters: {finding.whyItMatters} Required fix: {finding.requiredFix}
                  </span>
                ) : (
                  <span style={{ display: 'block', fontSize: 12, color: 'var(--fl-ink2)' }}>
                    Finding detail lives in the durable critic review record.
                  </span>
                )}

                {disposition ? (
                  <span style={{ display: 'block', fontSize: 12.5, marginTop: 4 }} data-testid="flow.review.disposition">
                    Disposition: <strong>{disposition.disposition}</strong> — {disposition.reason} (by{' '}
                    {disposition.decidedByUserId})
                  </span>
                ) : (
                  <span style={{ display: 'flex', gap: 6, marginTop: 6, flexWrap: 'wrap' }}>
                    <select
                      className="fl-select"
                      value={kindByFinding[findingId] ?? 'AcceptRisk'}
                      onChange={(event) => setKindByFinding((prev) => ({ ...prev, [findingId]: event.target.value }))}
                      data-testid="flow.review.dispositionKind"
                    >
                      <option value="AcceptRisk">Accept the risk</option>
                      <option value="FixInFollowUp">Fix in follow-up</option>
                      <option value="Reject">Reject the finding</option>
                    </select>
                    <input
                      style={{ flex: 1, minWidth: 180 }}
                      placeholder="Reason (required — a disposition without a reason is a dismissal)"
                      value={reasonByFinding[findingId] ?? ''}
                      onChange={(event) => setReasonByFinding((prev) => ({ ...prev, [findingId]: event.target.value }))}
                      data-testid="flow.review.dispositionReason"
                    />
                    <button
                      className="fl-btn"
                      disabled={busyAction !== null || (reasonByFinding[findingId] ?? '').trim().length === 0}
                      onClick={() =>
                        onRecordDisposition(findingId, kindByFinding[findingId] ?? 'AcceptRisk', reasonByFinding[findingId] ?? '')
                      }
                      data-testid="flow.review.recordDisposition"
                    >
                      Record disposition
                    </button>
                  </span>
                )}
              </span>
            </div>
          );
        })}
      </div>

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 8, flexWrap: 'wrap' }}>
        <button
          className="fl-btn"
          disabled={busyAction !== null}
          onClick={onRequestCriticReview}
          data-testid="flow.review.requestCritic"
        >
          {busyAction === 'critic' ? 'Reviewing…' : 'Request critic review'}
        </button>
        <span style={{ fontSize: 12.5 }} data-testid="flow.review.findingsGate">
          {undispositionedIds.length === 0
            ? criticReviews.length > 0
              ? 'Every finding carries a human disposition.'
              : 'No findings recorded.'
            : `${undispositionedIds.length} finding(s) await a human disposition — continuation is blocked until each is answered.`}
        </span>
      </div>
      <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
        Findings are advisory — a finding is not a veto. A disposition is a human decision about a finding; it is not
        approval, and continuation still requires its own live accepted approval.
      </p>
    </>
  );
}
