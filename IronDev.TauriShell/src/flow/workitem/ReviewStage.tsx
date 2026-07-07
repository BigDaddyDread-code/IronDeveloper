import type { SkeletonCriticPackage, SkeletonCriticReviewOutcome, SkeletonRunReport } from '../../api/types';
import { ApprovalGate } from './ApprovalGate';
import { CriterionTestMatrix } from './CriterionTestMatrix';
import { CriticPackageViewer } from './CriticPackageViewer';
import { FindingsPanel } from './FindingsPanel';

// Review stage — the stage orchestrates; the sections render.
//
// Boundary: everything actionable here is a REQUEST to a governed backend
// surface that enforces its own gate. The critic's findings are advisory,
// every finding needs a human disposition, an approval binds to the reviewed
// package hash, and the backend verifies all of it live. The UI records and
// requests; it can never grant.

interface ReviewStageProps {
  criticPackage: SkeletonCriticPackage | null;
  report: SkeletonRunReport | null;
  criticOutcome: SkeletonCriticReviewOutcome | null;
  busyAction: string | null;
  onRequestCriticReview: () => void;
  onRecordDisposition: (findingId: string, disposition: string, reason: string) => void;
  onRecordApproval: (reason: string) => void;
  onRequestContinuation: () => void;
  onRequestApply: () => void;
}

export function ReviewStage({
  criticPackage,
  report,
  criticOutcome,
  busyAction,
  onRequestCriticReview,
  onRecordDisposition,
  onRecordApproval,
  onRequestContinuation,
  onRequestApply
}: ReviewStageProps) {
  if (criticPackage === null) {
    return (
      <>
        <p className="fl-plabel">Review</p>
        <p className="fl-empty">Critic package not loaded. The package is prepared when the build run halts for approval.</p>
      </>
    );
  }

  const criticReviews = report?.criticReviews ?? [];
  const dispositions = report?.findingDispositions ?? [];
  const dispositionedIds = new Set(dispositions.map((disposition) => disposition.findingId));
  const hasUndispositionedFindings = criticReviews
    .flatMap((review) => review.findingIds)
    .some((findingId) => !dispositionedIds.has(findingId));

  const repairAttempts = report?.repairAttempts ?? [];

  return (
    <>
      {repairAttempts.length > 0 ? (
        <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.review.repairedNote">
          This run self-repaired {repairAttempts.length === 1 ? 'once' : `${repairAttempts.length} times`} before reaching
          review — the critic reviewed the repaired proposal{report?.proposal ? ` (${report.proposal.proposalId})` : ''}, and
          the gate below is unchanged.
        </p>
      ) : null}
      {report?.criticPackage ? (
        <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.review.packageFacts">
          Package {report.criticPackage.packageId} · announced hash{' '}
          <code>{report.criticPackage.announcedSha256.slice(0, 12)}…</code> ·{' '}
          {report.criticPackage.hashVerified ? (
            'hash verified against disk ✓'
          ) : (
            <strong style={{ color: 'var(--fl-red, #a63232)' }}>
              hash NOT verified — the file on disk does not match what the halt announced
            </strong>
          )}
          {' · '}
          {report.criticPackage.existsOnDisk ? 'evidence on disk' : 'EVIDENCE MISSING FROM DISK'}
          {' · '}
          criteria: {report.criticPackage.criterionCount - report.criticPackage.uncoveredCriterionCount}/
          {report.criticPackage.criterionCount} covered
          {report.criticPackage.uncoveredCriterionCount > 0
            ? ' — uncovered criteria are part of what any approval consciously owns'
            : ''}
        </p>
      ) : null}
      <CriticPackageViewer criticPackage={criticPackage} />
      <CriterionTestMatrix criticPackage={criticPackage} />
      <FindingsPanel
        criticOutcome={criticOutcome}
        criticReviews={criticReviews}
        dispositions={dispositions}
        busyAction={busyAction}
        onRequestCriticReview={onRequestCriticReview}
        onRecordDisposition={onRecordDisposition}
      />
      {report?.approval ? (
        <ApprovalGate
          approval={report.approval}
          hasUndispositionedFindings={hasUndispositionedFindings}
          busyAction={busyAction}
          onRecordApproval={onRecordApproval}
          onRequestContinuation={onRequestContinuation}
          onRequestApply={onRequestApply}
        />
      ) : (
        <>
          <p className="fl-plabel" style={{ marginTop: 14 }}>
            Human gate
          </p>
          <p className="fl-empty">No approval requirement recorded yet.</p>
        </>
      )}
    </>
  );
}
