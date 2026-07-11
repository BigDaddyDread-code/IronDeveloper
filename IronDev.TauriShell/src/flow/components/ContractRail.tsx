import { DraftCriterion, DraftOpenQuestion } from '../flowTypes';

interface ContractRailProps {
  criteria: DraftCriterion[];
  openQuestions: DraftOpenQuestion[];
  architectureRefs: string[];
  summary?: {
    criterionCount: number;
    affectedFileCount: number;
  };
  onConfirmCriterion?: (id: string) => void;
  onResolveQuestion?: (id: string) => void;
}

export function ContractRail({
  criteria,
  openQuestions,
  architectureRefs,
  summary,
  onConfirmCriterion,
  onResolveQuestion
}: ContractRailProps) {
  const unresolved = openQuestions.filter((q) => !q.resolved);

  return (
    <div className="fl-panel-box" data-testid="flow.contract">
      <p className="fl-plabel">The contract</p>
      {summary ? (
        <p className="fl-contract-summary" data-testid="flow.contract.summary">
          {summary.criterionCount} {summary.criterionCount === 1 ? 'criterion' : 'criteria'} · {summary.affectedFileCount}{' '}
          affected {summary.affectedFileCount === 1 ? 'file' : 'files'}
        </p>
      ) : null}

      {criteria.length === 0 ? (
        <p className="fl-empty">No acceptance criteria yet. Shape the work in the discussion.</p>
      ) : (
        criteria.map((criterion) => (
          <div className="fl-crit" key={criterion.id}>
            <span className={criterion.confirmed ? 'fl-crit-mark fl-yes' : 'fl-crit-mark fl-pend'}>
              {criterion.confirmed ? '✓' : '…'}
            </span>
            <span>{criterion.text}</span>
            {!criterion.confirmed && onConfirmCriterion ? (
              <span className="fl-crit-actions">
                <button className="fl-btn fl-mini" onClick={() => onConfirmCriterion(criterion.id)}>
                  Confirm
                </button>
              </span>
            ) : null}
          </div>
        ))
      )}

      <p className="fl-plabel" style={{ marginTop: 16 }}>
        Architecture riding along
      </p>
      {architectureRefs.length === 0 ? (
        <p className="fl-empty">No decisions or standards attached yet.</p>
      ) : (
        <div className="fl-chips">
          {architectureRefs.map((ref) => (
            <span className="fl-chip" key={ref}>
              {ref}
            </span>
          ))}
        </div>
      )}

      {unresolved.map((question) => (
        <div className="fl-qbox" key={question.id}>
          <span>Open question — {question.text}</span>
          {onResolveQuestion ? (
            <button className="fl-btn fl-mini" onClick={() => onResolveQuestion(question.id)}>
              Resolve
            </button>
          ) : null}
        </div>
      ))}
    </div>
  );
}
