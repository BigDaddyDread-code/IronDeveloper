import type { SkeletonCriticPackage } from '../../api/types';

// P1-4 on screen: the criterion→test matrix, one row per acceptance criterion —
// covered by named tests or explicitly UNCOVERED. A hole is part of what the
// human approves, so it is rendered, never elided.

interface CriterionTestMatrixProps {
  criticPackage: SkeletonCriticPackage;
}

export function CriterionTestMatrix({ criticPackage }: CriterionTestMatrixProps) {
  const coverage = criticPackage.criterionCoverage ?? [];
  const uncoveredCount = coverage.filter((row) => !row.covered).length;

  return (
    <>
      <p className="fl-plabel" style={{ marginTop: 14 }}>
        Criterion → test matrix
      </p>
      {coverage.length === 0 ? (
        <p className="fl-empty" data-testid="flow.review.matrix">
          The matrix has no cells: no checkable acceptance criteria were parsed for this run. That absence is visible by
          design — it is part of what you are approving.
        </p>
      ) : (
        <>
          <table className="fl-table" data-testid="flow.review.matrix">
            <thead>
              <tr>
                <th>Criterion</th>
                <th>Coverage</th>
              </tr>
            </thead>
            <tbody>
              {coverage.map((row) => (
                <tr key={row.criterion}>
                  <td>{row.criterion}</td>
                  <td>
                    {row.covered ? (
                      row.coveringTests.join(', ')
                    ) : (
                      <span style={{ color: 'var(--fl-gate-ink)', fontWeight: 600 }} data-testid="flow.review.uncovered">
                        UNCOVERED — no authored test checks this criterion
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {uncoveredCount > 0 ? (
            <p style={{ fontSize: 12.5, color: 'var(--fl-gate-ink)' }}>
              {uncoveredCount} criterion{uncoveredCount > 1 ? 'a have' : ' has'} no covering test. Approving this run
              includes consciously owning that coverage hole.
            </p>
          ) : null}
        </>
      )}
      <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
        Authored tests derive from the acceptance criteria and never see the builder&apos;s diff. They ran in the disposable
        workspace; they are not applied to the source repository.
      </p>
    </>
  );
}
