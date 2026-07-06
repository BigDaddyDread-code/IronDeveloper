import type { SkeletonRunProposalTrace, SkeletonRunRepairAttemptTrace } from '../../api/types';

// REPAIR-1 surfaced honestly. A run that needed bounded repair says so: every
// attempt is listed with what failed and which model repaired it, and the
// initial (failed) proposal is explicitly distinguished from the proposal the
// gate binds to. Renders NOTHING when no repair happened — the boring good
// case earns no noise.
//
// Boundary: a repair attempt is proposal-shaped work, never authority. The
// human gate after a repaired attempt is exactly the gate; this panel records
// history, it grants nothing.

interface RepairAttemptsPanelProps {
  repairAttempts: SkeletonRunRepairAttemptTrace[];
  initialProposal?: SkeletonRunProposalTrace | null;
  gateProposal?: SkeletonRunProposalTrace | null;
}

export function RepairAttemptsPanel({ repairAttempts, initialProposal, gateProposal }: RepairAttemptsPanelProps) {
  if (repairAttempts.length === 0) {
    return null;
  }

  return (
    <div data-testid="flow.build.repairAttempts">
      <p className="fl-plabel" style={{ marginTop: 14 }}>
        Bounded repair
      </p>
      {repairAttempts.map((attempt) => (
        <div className="fl-qbox" key={attempt.attemptNumber} data-testid={`flow.build.repairAttempt.${attempt.attemptNumber}`}>
          <span>
            <strong style={{ fontSize: 12.5 }}>
              Attempt {attempt.attemptNumber} — repaired after {attempt.failureKind || 'a failure'}
              {attempt.failedCommand ? ` on '${attempt.failedCommand}'` : ''}
            </strong>
            <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>
              Repair proposal {attempt.repairProposalId || '(not recorded)'}
              {attempt.modelName ? ` · repaired by ${attempt.modelProvider}/${attempt.modelName}` : ''}
              {attempt.repairProposalEvidenceExistsOnDisk ? '' : ' · repair proposal evidence missing from disk'}
            </span>
          </span>
        </div>
      ))}
      {initialProposal ? (
        <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.build.initialProposal">
          Initial proposal {initialProposal.proposalId} failed and is preserved as history — the proposal under review is{' '}
          {gateProposal ? gateProposal.proposalId : 'the repaired proposal'}.
        </p>
      ) : null}
      <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }} data-testid="flow.build.repairBoundary">
        A repair attempt is a new proposal, not authority — the human gate is unchanged.
      </p>
    </div>
  );
}
