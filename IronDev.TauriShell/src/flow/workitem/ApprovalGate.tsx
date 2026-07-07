import { useState } from 'react';
import type { SkeletonRunApprovalTrace } from '../../api/types';

// APPROVAL-UX-1: the human gate, with ceremony proportional to authority consumed
// (full-ux-map §9.2). Recording an approval is no longer one click: the approver
// types a reason (it becomes durable evidence on the approval record) and confirms
// the package hash prefix by typing it — proof of looking, not of scrolling past.
//
// Boundary: every button is a REQUEST to a governed endpoint that enforces its
// own gate. Recording is not continuation; continuation is not apply permission.
// The ceremony adds friction, never authority — the backend verifies everything live.

const HashConfirmationLength = 8;

interface ApprovalGateProps {
  approval: SkeletonRunApprovalTrace;
  hasUndispositionedFindings: boolean;
  busyAction: string | null;
  onRecordApproval: (reason: string) => void;
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
  const [ceremonyOpen, setCeremonyOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [hashConfirmation, setHashConfirmation] = useState('');

  const expectedPrefix = approval.targetHash.slice(0, HashConfirmationLength);
  const hashConfirmed =
    expectedPrefix.length > 0 && hashConfirmation.trim().toLowerCase() === expectedPrefix.toLowerCase();
  const reasonGiven = reason.trim().length > 0;
  const ceremonyComplete = hashConfirmed && reasonGiven;

  const unmet: string[] = [];
  if (!reasonGiven) {
    unmet.push('a reason is required — it becomes durable evidence on the approval record');
  }
  if (!hashConfirmed) {
    unmet.push(`type the first ${HashConfirmationLength} characters of the package hash to confirm what you reviewed`);
  }

  const confirmCeremony = () => {
    if (!ceremonyComplete) {
      return;
    }
    onRecordApproval(reason.trim());
    setCeremonyOpen(false);
    setReason('');
    setHashConfirmation('');
  };

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
      <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.review.delegatedPolicy">
        Delegated approval: none exists — the default. Every continuation requires explicit human approval; the policy
        draft in Settings requests future policy, it grants nothing. The local alpha permits the run author to approve;
        the record names the actor either way.
      </p>
      {hasUndispositionedFindings ? (
        <p style={{ fontSize: 12.5, color: 'var(--fl-gate-ink)' }}>
          Critic findings await dispositions — the backend will refuse continuation until every finding is answered, no
          matter what approvals exist.
        </p>
      ) : null}

      {ceremonyOpen && !approval.continuationUnblocked ? (
        <div className="fl-qbox" data-testid="flow.review.approvalCeremony">
          <span style={{ width: '100%', display: 'grid', gap: 6 }}>
            <strong style={{ fontSize: 12.5 }}>Record accepted approval — say why, confirm what</strong>
            <textarea
              className="fl-select"
              style={{ width: '100%', minHeight: 54, resize: 'vertical' }}
              placeholder="Why does this package deserve to continue? The reason is recorded as evidence on the approval."
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              data-testid="flow.review.approvalReason"
            />
            <span style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <input
                className="fl-select"
                style={{ width: 160, fontFamily: 'var(--fl-mono, monospace)' }}
                placeholder={`Hash prefix (${HashConfirmationLength} chars)`}
                value={hashConfirmation}
                onChange={(event) => setHashConfirmation(event.target.value)}
                data-testid="flow.review.approvalHashConfirmation"
              />
              <span style={{ fontSize: 12, color: hashConfirmed ? 'var(--fl-acc-ink, inherit)' : 'var(--fl-muted)' }}>
                {hashConfirmed ? 'Hash confirmed.' : `Expected prefix of ${approval.targetHash.slice(0, 12)}…`}
              </span>
            </span>
            {!ceremonyComplete ? (
              <span style={{ fontSize: 12, color: 'var(--fl-gate-ink)' }} data-testid="flow.review.ceremonyUnmet">
                Before recording: {unmet.join('; ')}.
              </span>
            ) : null}
            <span style={{ display: 'flex', gap: 8 }}>
              <button
                className="fl-btn fl-pri"
                disabled={busyAction !== null || !ceremonyComplete}
                onClick={confirmCeremony}
                data-testid="flow.review.confirmApproval"
              >
                {busyAction === 'record' ? 'Recording…' : 'Record approval with this reason'}
              </button>
              <button className="fl-btn" disabled={busyAction !== null} onClick={() => setCeremonyOpen(false)}>
                Cancel
              </button>
            </span>
          </span>
        </div>
      ) : null}

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {!ceremonyOpen ? (
          <button
            className="fl-btn fl-pri"
            disabled={busyAction !== null || approval.continuationUnblocked}
            onClick={() => setCeremonyOpen(true)}
            data-testid="flow.review.recordApproval"
          >
            Record my approval…
          </button>
        ) : null}
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
