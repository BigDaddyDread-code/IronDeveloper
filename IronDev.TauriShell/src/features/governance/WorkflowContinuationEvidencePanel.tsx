import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsWorkflowContinuationEvidenceAuthorityClaim,
  containsWorkflowContinuationEvidenceUnsafeText,
  workflowContinuationEvidenceBoundaryBanner,
  workflowContinuationEvidenceBoundaryRules,
  workflowContinuationEvidenceSafeText
} from './WorkflowContinuationEvidenceBoundary';
import type { WorkflowContinuationEvidence, WorkflowContinuationEvidencePanelProps } from './WorkflowContinuationEvidenceTypes';
import {
  hasInvalidWorkflowContinuationEvidenceTimestamp,
  hasWorkflowContinuationEvidenceAuthorityFlags,
  missingWorkflowContinuationEvidenceFields,
  workflowContinuationEvidenceDefaultDisplayState
} from './WorkflowContinuationEvidenceTypes';

export function WorkflowContinuationEvidencePanel({ evidence = null, isLoading = false, errorMessage = null }: WorkflowContinuationEvidencePanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? workflowContinuationEvidenceDefaultDisplayState;
  const missingFields = useMemo(() => missingWorkflowContinuationEvidenceFields(evidence), [evidence]);
  const invalidTimestamp =
    hasInvalidWorkflowContinuationEvidenceTimestamp(evidence?.reviewedAtUtc) ||
    hasInvalidWorkflowContinuationEvidenceTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasWorkflowContinuationEvidenceAuthorityFlags(evidence);
  const missingEvidenceRefs = evidence?.evidenceRefs.length === 0;
  const missingBoundaryMaxims = evidence?.boundaryMaxims.length === 0;
  const missingContinuationParts = Boolean(evidence) && (!evidence?.continuationGatePresent || !evidence?.continuationGateSatisfied);
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp || missingBoundaryMaxims || missingContinuationParts;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const partial = evidence?.workflowContinuationPartial === true;
  const failed = evidence?.workflowContinuationFailed === true;
  const mutationDetected = evidence?.workflowMutationDetected === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.recordStored &&
    displayState.humanReviewRequired &&
    evidence?.continuationGatePresent === true &&
    evidence?.continuationGateSatisfied === true &&
    !incomplete &&
    !missingEvidenceRefs &&
    !stale &&
    !expired &&
    !partial &&
    !failed &&
    !mutationDetected &&
    !unsafeTextDetected &&
    !authorityClaimDetected &&
    !authorityFlagsDetected;

  const copyValue = useCallback((value: string | undefined, label: string) => {
    if (!value?.trim()) {
      setCopyMessage(`${label} is missing. Nothing was copied.`);
      return;
    }

    void navigator.clipboard?.writeText(value).catch(() => undefined);
    setCopyMessage(`${label} copied for inspection only. Copying workflow continuation evidence does not continue workflow.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="workflow-continuation-evidence.workspace">
        <WorkflowContinuationEvidenceBoundaryBanner />
        <Surface testId="workflow-continuation-evidence.loading">
          <EmptyState title="Loading workflow continuation evidence..." body="UI loading does not approve continuation, continue workflow, or create transition records." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="workflow-continuation-evidence.workspace">
        <WorkflowContinuationEvidenceBoundaryBanner />
        <Surface testId="workflow-continuation-evidence.error">
          <EmptyState title="Unable to load workflow continuation evidence." body="No approval, workflow transition, source mutation, recovery, or workflow state changed." />
          <p className="state-muted">{workflowContinuationEvidenceSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="workflow-continuation-evidence.workspace">
        <WorkflowContinuationEvidenceBoundaryBanner />
        <Surface testId="workflow-continuation-evidence.empty">
          <EmptyState title="No workflow continuation evidence selected." body="Missing workflow continuation evidence does not permit workflow continuation." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="workflow-continuation-evidence.workspace">
      <WorkflowContinuationEvidenceBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Workflow continuation evidence</p>
            <h2>Workflow Continuation Evidence</h2>
            <p>
              This view displays supplied workflow continuation evidence in one place. It does not approve continuation, continue workflow, create transition records, or make UI state authoritative.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="workflow-continuation-evidence.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims continuation satisfaction' : 'Supplied evidence does not claim continuation satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="workflow-continuation-evidence.identity">
            <div className="section-heading">
              <p className="eyebrow">Evidence identity</p>
              <h3>Evidence, not continuation</h3>
            </div>
            <MetadataRow label="Workflow continuation evidence id" value={workflowContinuationEvidenceSafeText(evidence.workflowContinuationEvidenceId)} />
            <MetadataRow label="Workflow continuation evidence hash" value={workflowContinuationEvidenceSafeText(evidence.workflowContinuationEvidenceHash)} />
            <MetadataRow label="Continuation status" value={workflowContinuationEvidenceSafeText(evidence.continuationStatus)} />
            <MetadataRow label="Reviewed by" value={workflowContinuationEvidenceSafeText(evidence.reviewedBy)} />
            <MetadataRow label="Reviewed" value={DateTimeDisplay.toLocalDisplay(workflowContinuationEvidenceSafeText(evidence.reviewedAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(workflowContinuationEvidenceSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="workflow-continuation-evidence.gateBinding">
            <div className="section-heading">
              <p className="eyebrow">Continuation gate</p>
              <h3>Gate evidence binding</h3>
            </div>
            <MetadataRow label="Continuation gate evaluation id" value={workflowContinuationEvidenceSafeText(evidence.continuationGateEvaluationId)} />
            <MetadataRow label="Continuation gate evaluation hash" value={workflowContinuationEvidenceSafeText(evidence.continuationGateEvaluationHash)} />
            <MetadataRow label="Continuation gate present" value={<BooleanBadge value={evidence.continuationGatePresent} />} />
            <MetadataRow label="Continuation gate satisfied elsewhere" value={<BooleanBadge value={evidence.continuationGateSatisfied} />} />
          </Surface>

          <Surface testId="workflow-continuation-evidence.transitionBinding">
            <div className="section-heading">
              <p className="eyebrow">Workflow transition record</p>
              <h3>Transition evidence, not transition by UI</h3>
            </div>
            <MetadataRow label="Workflow transition record id" value={workflowContinuationEvidenceSafeText(evidence.workflowTransitionRecordId)} />
            <MetadataRow label="Workflow transition record hash" value={workflowContinuationEvidenceSafeText(evidence.workflowTransitionRecordHash)} />
            <MetadataRow label="Workflow transition record present" value={<BooleanBadge value={evidence.transitionRecordPresent} />} />
            <MetadataRow label="Workflow transition record valid" value={<BooleanBadge value={evidence.transitionRecordValid} />} />
          </Surface>

          <Surface testId="workflow-continuation-evidence.sourceApplyBinding">
            <div className="section-heading">
              <p className="eyebrow">Source apply evidence</p>
              <h3>Optional binding</h3>
            </div>
            <MetadataRow label="Source apply receipt id" value={workflowContinuationEvidenceSafeText(evidence.sourceApplyReceiptId)} />
            <MetadataRow label="Source apply receipt hash" value={workflowContinuationEvidenceSafeText(evidence.sourceApplyReceiptHash)} />
          </Surface>

          <Surface testId="workflow-continuation-evidence.rollbackBinding">
            <div className="section-heading">
              <p className="eyebrow">Rollback evidence</p>
              <h3>Optional binding</h3>
            </div>
            <MetadataRow label="Rollback execution receipt id" value={workflowContinuationEvidenceSafeText(evidence.rollbackExecutionReceiptId)} />
            <MetadataRow label="Rollback execution receipt hash" value={workflowContinuationEvidenceSafeText(evidence.rollbackExecutionReceiptHash)} />
          </Surface>

          <Surface testId="workflow-continuation-evidence.subjectBinding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Bound evidence</h3>
            </div>
            <MetadataRow label="Subject kind" value={workflowContinuationEvidenceSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={workflowContinuationEvidenceSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={workflowContinuationEvidenceSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={workflowContinuationEvidenceSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={workflowContinuationEvidenceSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="workflow-continuation-evidence.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied continuation state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Workflow continued elsewhere" value={<BooleanBadge value={evidence.workflowContinuedElsewhere} />} />
            <MetadataRow label="Continuation partial elsewhere" value={<BooleanBadge value={evidence.workflowContinuationPartial} />} />
            <MetadataRow label="Continuation failed elsewhere" value={<BooleanBadge value={evidence.workflowContinuationFailed} />} />
            <MetadataRow label="Workflow mutation detected" value={<BooleanBadge value={evidence.workflowMutationDetected} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="workflow-continuation-evidence.currentBadge">Supplied workflow continuation evidence is available.</p> : null}
          </Surface>

          <Surface testId="workflow-continuation-evidence.steps">
            <div className="section-heading">
              <p className="eyebrow">Step summary</p>
              <h3>Summary only</h3>
            </div>
            {evidence.stepSummaries.length === 0 ? (
              <p data-testid="workflow-continuation-evidence.noSteps">No step summary supplied. Missing summary does not permit workflow continuation.</p>
            ) : (
              <ul className="detail-list">
                {evidence.stepSummaries.map((step, index) => (
                  <li key={`${step.stepId}-${index}`}>
                    <strong>{workflowContinuationEvidenceSafeText(step.status)}</strong>: {workflowContinuationEvidenceSafeText(step.stepName)} ({workflowContinuationEvidenceSafeText(step.stepId)})
                    {' - '}{workflowContinuationEvidenceSafeText(step.safeSummary)}
                  </li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="workflow-continuation-evidence.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="workflow-continuation-evidence.noEvidenceRefs">No evidence references supplied. Missing evidence does not permit workflow continuation.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{workflowContinuationEvidenceSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="workflow-continuation-evidence.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="workflow-continuation-evidence.incompleteWarning">
                Workflow continuation evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : missingBoundaryMaxims ? 'boundaryMaxims' : missingContinuationParts ? 'continuation gate evidence binding' : 'invalid timestamp'}.
              </p>
            ) : null}
            {missingEvidenceRefs ? <p data-testid="workflow-continuation-evidence.missingEvidenceWarning">Workflow continuation evidence has no evidence references and cannot permit workflow continuation.</p> : null}
            {stale ? <p data-testid="workflow-continuation-evidence.staleWarning">Workflow continuation evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="workflow-continuation-evidence.expiredWarning">Workflow continuation evidence is expired. This UI will not renew evidence.</p> : null}
            {partial ? <p data-testid="workflow-continuation-evidence.partialWarning">Workflow continuation evidence says continuation was partial elsewhere. This UI will not retry continuation or declare continuation complete.</p> : null}
            {failed ? <p data-testid="workflow-continuation-evidence.failureWarning">Workflow continuation evidence says continuation failed elsewhere. This UI will not start recovery, source apply, rollback, or workflow continuation.</p> : null}
            {mutationDetected ? <p data-testid="workflow-continuation-evidence.mutationWarning">Workflow mutation was detected in supplied evidence. This UI will not normalize it into approval, release readiness, or continuation authority.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="workflow-continuation-evidence.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="workflow-continuation-evidence.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="workflow-continuation-evidence.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{workflowContinuationEvidenceSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="workflow-continuation-evidence.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {workflowContinuationEvidenceBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="workflow-continuation-evidence.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.workflowContinuationEvidenceId, 'Workflow continuation evidence id')}>
            Copy Workflow Continuation Evidence ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.workflowContinuationEvidenceHash, 'Workflow continuation evidence hash')}>
            Copy Workflow Continuation Evidence Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.continuationGateEvaluationHash, 'Continuation gate evaluation hash')}>
            Copy Continuation Gate Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="workflow-continuation-evidence.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function WorkflowContinuationEvidenceBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="workflow-continuation-evidence.boundaryBanner">
      <div className="approval-package-banner">
        <span>{workflowContinuationEvidenceBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: WorkflowContinuationEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.workflowContinuationEvidenceId,
    evidence.workflowContinuationEvidenceHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.continuationGateEvaluationId,
    evidence.continuationGateEvaluationHash,
    evidence.workflowTransitionRecordId,
    evidence.workflowTransitionRecordHash,
    evidence.sourceApplyReceiptId,
    evidence.sourceApplyReceiptHash,
    evidence.rollbackExecutionReceiptId,
    evidence.rollbackExecutionReceiptHash,
    evidence.reviewedBy,
    evidence.continuationStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.stepSummaries.flatMap((step) => [step.stepId, step.stepName, step.status, step.safeSummary])
  ].some(containsWorkflowContinuationEvidenceUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: WorkflowContinuationEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.workflowContinuationEvidenceId,
    evidence.workflowContinuationEvidenceHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.continuationGateEvaluationId,
    evidence.continuationGateEvaluationHash,
    evidence.workflowTransitionRecordId,
    evidence.workflowTransitionRecordHash,
    evidence.sourceApplyReceiptId,
    evidence.sourceApplyReceiptHash,
    evidence.rollbackExecutionReceiptId,
    evidence.rollbackExecutionReceiptHash,
    evidence.reviewedBy,
    evidence.continuationStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.stepSummaries.flatMap((step) => [step.stepId, step.stepName, step.status, step.safeSummary])
  ].some(containsWorkflowContinuationEvidenceAuthorityClaim);
}
