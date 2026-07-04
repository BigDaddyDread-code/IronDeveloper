import type { SkeletonRunApprovalTrace } from '../../api/types';

// The human gate. Every button is a REQUEST to a governed endpoint that
// enforces its own gate: recording an approval binds a human decision to the
// exact package hash reviewed; continuation and apply are verified live by the
// backend. Recording is not continuation; continuation is not apply permission.

interface ApprovalGateProps {
  approval: SkeletonRunApprovalTrace;
  hasUndispositionedFindings: boolean;
  busyAction: string | null;
  onRecordApproval: () => void;
  onRequestContinuation: () => void;
  onRequestApply: () => void;
}

export function ApprovalGate({
  approval,
  hasUndispositionedFindings,
  busyAction,
  onRecordApproval,
  onRequestContinuation,
  onRequestApply
}: ApprovalGateProps) {
  return (
    <>
      <p className="fl-plabel" style={{ marginTop: 14 }}>
        Human gate
      </p>
      <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.review.requirement">
        Requirement: {approval.capabilityCode} on {approval.targetKind} · bound to package hash{' '}
        <code>{approval.targetHash.slice(0, 12)}…</code>. An approval attaches to exactly what was reviewed — if the package
        changes, the approval no longer satisfies.
      </p>
      {hasUndispositionedFindings ? (
        <p style={{ fontSize: 12.5, color: 'var(--fl-gate-ink)' }}>
          Critic findings await dispositions — the backend will refuse continuation until every finding is answered, no
          matter what approvals exist.
        </p>
      ) : null}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <button
          className="fl-btn fl-pri"
          disabled={busyAction !== null || approval.continuationUnblocked}
          onClick={onRecordApproval}
          data-testid="flow.review.recordApproval"
        >
          {busyAction === 'record' ? 'Recording…' : 'Record my approval'}
        </button>
        <button
          className="fl-btn"
          disabled={busyAction !== null || approval.continuationUnblocked}
          onClick={onRequestContinuation}
          data-testid="flow.review.requestContinuation"
        >
          {busyAction === 'continue' ? 'Requesting…' : 'Request continuation'}
        </button>
        <button
          className="fl-btn"
          disabled={busyAction !== null || !approval.continuationUnblocked}
          onClick={onRequestApply}
          data-testid="flow.review.requestApply"
        >
          {busyAction === 'apply' ? 'Requesting…' : 'Request controlled apply'}
        </button>
      </div>
      <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
        Recording an approval is a human decision entering the record — it is not continuation, and continuation is not
        apply permission. Each request is verified live by the backend against its own evidence; a refusal names its reason.
      </p>
    </>
  );
}
