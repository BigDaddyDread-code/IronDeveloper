import { useState } from 'react';
import type { SkeletonCriticPackage, SkeletonRunReport } from '../../api/types';

// Review stage — the critic package at full fidelity plus the human gate.
//
// Boundary: everything actionable here is a REQUEST to a governed backend
// surface that enforces its own gate. Recording an approval binds a human
// decision to the exact package hash that was reviewed; it is not policy
// satisfaction, not continuation, not apply. Continuation and apply are
// requests the backend verifies live and refuses on its own evidence.
// The UI records and requests; it can never grant.

interface ReviewStageProps {
  criticPackage: SkeletonCriticPackage | null;
  report: SkeletonRunReport | null;
  busyAction: string | null;
  onRecordApproval: () => void;
  onRequestContinuation: () => void;
  onRequestApply: () => void;
}

export function ReviewStage({
  criticPackage,
  report,
  busyAction,
  onRecordApproval,
  onRequestContinuation,
  onRequestApply
}: ReviewStageProps) {
  const [openDiff, setOpenDiff] = useState<string | null>(null);
  const approval = report?.approval ?? null;

  if (criticPackage === null) {
    return (
      <>
        <p className="fl-plabel">Review</p>
        <p className="fl-empty">Critic package not loaded. The package is prepared when the build run halts for approval.</p>
      </>
    );
  }

  return (
    <>
      <p className="fl-plabel">Proposed change — full fidelity</p>
      <p style={{ fontSize: 13.5, marginTop: 0 }}>{criticPackage.proposalSummary || 'No summary.'}</p>
      <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }}>
        Workspace build/test: {criticPackage.workspaceRunSucceeded ? 'succeeded' : 'FAILED'} ·{' '}
        {criticPackage.commandResults.map((command) => `${command.displayName} (exit ${command.exitCode})`).join(' · ') || 'no commands recorded'}
      </p>

      <div data-testid="flow.review.changes">
        {criticPackage.changes.map((change) => (
          <div className="fl-qbox" key={change.filePath}>
            <span style={{ width: '100%' }}>
              <strong style={{ fontSize: 12.5 }}>
                {change.filePath}
                {change.isNewFile ? ' · new' : change.isDeletion ? ' · deletion' : ''}
              </strong>
              {change.description ? (
                <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>{change.description}</span>
              ) : null}
              <button
                className="fl-btn"
                style={{ marginTop: 6 }}
                onClick={() => setOpenDiff((prev) => (prev === change.filePath ? null : change.filePath))}
              >
                {openDiff === change.filePath ? 'Hide diff' : 'Show diff'}
              </button>
              {openDiff === change.filePath ? (
                <pre
                  style={{
                    fontSize: 11.5,
                    overflowX: 'auto',
                    background: 'var(--fl-bg2, rgba(0,0,0,0.04))',
                    padding: 8,
                    borderRadius: 6
                  }}
                >
                  {change.diff || change.fullContentAfter || '(no diff recorded)'}
                </pre>
              ) : null}
            </span>
          </div>
        ))}
      </div>

      <p className="fl-plabel" style={{ marginTop: 14 }}>
        Criterion → test matrix
      </p>
      {criticPackage.authoredTests.length === 0 ? (
        <p className="fl-empty" data-testid="flow.review.matrix">
          The matrix has no cells: no tests were authored for this run. That absence is visible by design — it is part of what
          you are approving.
        </p>
      ) : (
        <table className="fl-table" data-testid="flow.review.matrix">
          <thead>
            <tr>
              <th>Criterion</th>
              <th>Authored test</th>
            </tr>
          </thead>
          <tbody>
            {criticPackage.authoredTests.map((test) => (
              <tr key={test.relativePath}>
                <td>{test.coversCriterion || '(criterion not named)'}</td>
                <td>{test.relativePath}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
        Authored tests derive from the acceptance criteria and never see the builder&apos;s diff. They ran in the disposable
        workspace; they are not applied to the source repository.
      </p>

      <p className="fl-plabel" style={{ marginTop: 14 }}>
        Human gate
      </p>
      {approval === null ? (
        <p className="fl-empty">No approval requirement recorded yet.</p>
      ) : (
        <>
          <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }} data-testid="flow.review.requirement">
            Requirement: {approval.capabilityCode} on {approval.targetKind} · bound to package hash{' '}
            <code>{approval.targetHash.slice(0, 12)}…</code>. An approval attaches to exactly what was reviewed — if the
            package changes, the approval no longer satisfies.
          </p>
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
            apply permission. Each request is verified live by the backend against its own evidence; a refusal names its
            reason.
          </p>
        </>
      )}
    </>
  );
}
