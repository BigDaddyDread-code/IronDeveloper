import type { SkeletonRunReport, TicketBuildRunDto } from '../../api/types';

// Build stage — the live view of a skeleton run, rendered entirely from the
// run report (P0-6): durable evidence, re-verified server-side at read time.
// Read-only surface: run visibility is not run authority, and the UI never
// invents a state the backend did not record.

interface BuildStageProps {
  run: TicketBuildRunDto;
  report: SkeletonRunReport | null;
  onRefreshReport: () => void;
}

export function statusTone(status: string): string {
  if (status === 'PausedForApproval' || status === 'Completed' || status === 'Applied') {
    return 'var(--fl-acc-ink)';
  }
  if (status === 'Failed' || status === 'Cancelled') {
    return 'var(--fl-gate-ink)';
  }
  return 'var(--fl-ink2)';
}

export function BuildStage({ run, report, onRefreshReport }: BuildStageProps) {
  return (
    <>
      <p className="fl-plabel">Build run</p>
      <p style={{ fontSize: 13.5, marginTop: 0 }} data-testid="flow.build.status">
        <span style={{ color: statusTone(run.status), fontWeight: 600 }}>{run.status}</span>
        {run.message ? <span style={{ color: 'var(--fl-ink2)' }}> — {run.message}</span> : null}
      </p>

      {report === null ? (
        <p className="fl-empty">Report not loaded yet.</p>
      ) : (
        <>
          {report.proposal ? (
            <p style={{ fontSize: 13, color: 'var(--fl-ink2)' }} data-testid="flow.build.proposal">
              Proposal {report.proposal.proposalId} · {report.proposal.fileChangeCount} file change(s)
              {report.proposal.modelName ? ` · built by ${report.proposal.modelProvider}/${report.proposal.modelName}` : ''}
            </p>
          ) : null}

          {report.testAuthoring ? (
            <p style={{ fontSize: 13, color: 'var(--fl-ink2)' }} data-testid="flow.build.testAuthoring">
              {report.testAuthoring.authored
                ? `${report.testAuthoring.authoredTestCount} test(s) authored from the acceptance criteria — blind to the builder's diff.` +
                  (report.testAuthoring.modelName ? ` (model: ${report.testAuthoring.modelProvider}/${report.testAuthoring.modelName})` : '')
                : `Test authoring skipped: ${report.testAuthoring.skippedReason} The criterion-to-test matrix has no cells and the review will say so.`}
            </p>
          ) : null}

          {report.gaps.length > 0 ? (
            <div className="fl-error" data-testid="flow.build.gaps">
              {report.gaps.map((gap) => (
                <div key={gap}>{gap}</div>
              ))}
            </div>
          ) : null}

          <p className="fl-plabel" style={{ marginTop: 14 }}>
            Timeline
          </p>
          <div data-testid="flow.build.timeline">
            {report.timeline.length === 0 ? (
              <p className="fl-empty">No events recorded.</p>
            ) : (
              report.timeline.map((entry, index) => (
                <div className="fl-qbox" key={`${entry.eventType}-${index}`}>
                  <span>
                    <strong style={{ fontSize: 12.5 }}>{entry.eventType}</strong>
                    <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>{entry.message}</span>
                  </span>
                </div>
              ))
            )}
          </div>

          <button className="fl-btn" style={{ marginTop: 10 }} onClick={onRefreshReport} data-testid="flow.build.refresh">
            Refresh report
          </button>
        </>
      )}
    </>
  );
}
