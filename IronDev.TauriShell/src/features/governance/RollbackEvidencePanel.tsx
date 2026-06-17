import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsRollbackEvidenceAuthorityClaim,
  containsRollbackEvidenceUnsafeText,
  rollbackEvidenceBoundaryBanner,
  rollbackEvidenceBoundaryRules,
  rollbackEvidenceSafeText
} from './RollbackEvidenceBoundary';
import type { RollbackEvidence, RollbackEvidencePanelProps } from './RollbackEvidenceTypes';
import {
  hasInvalidRollbackEvidenceTimestamp,
  hasRollbackEvidenceAuthorityFlags,
  missingRollbackEvidenceFields,
  rollbackEvidenceDefaultDisplayState
} from './RollbackEvidenceTypes';

export function RollbackEvidencePanel({ evidence = null, isLoading = false, errorMessage = null }: RollbackEvidencePanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? rollbackEvidenceDefaultDisplayState;
  const missingFields = useMemo(() => missingRollbackEvidenceFields(evidence), [evidence]);
  const invalidTimestamp =
    hasInvalidRollbackEvidenceTimestamp(evidence?.reviewedAtUtc) ||
    hasInvalidRollbackEvidenceTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasRollbackEvidenceAuthorityFlags(evidence);
  const missingEvidenceRefs = evidence?.evidenceRefs.length === 0;
  const missingBoundaryMaxims = evidence?.boundaryMaxims.length === 0;
  const missingRollbackParts = Boolean(evidence) && (!evidence?.rollbackPlanPresent || !evidence?.rollbackSupportReceiptPresent);
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp || missingBoundaryMaxims || missingRollbackParts;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const partial = evidence?.rollbackPartial === true;
  const failed = evidence?.rollbackFailed === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.recordStored &&
    displayState.humanReviewRequired &&
    evidence?.rollbackPlanPresent === true &&
    evidence?.rollbackSupportReceiptPresent === true &&
    !incomplete &&
    !missingEvidenceRefs &&
    !stale &&
    !expired &&
    !unsafeTextDetected &&
    !authorityClaimDetected &&
    !authorityFlagsDetected;

  const copyValue = useCallback((value: string | undefined, label: string) => {
    if (!value?.trim()) {
      setCopyMessage(`${label} is missing. Nothing was copied.`);
      return;
    }

    void navigator.clipboard?.writeText(value).catch(() => undefined);
    setCopyMessage(`${label} copied for inspection only. Copying rollback evidence does not execute rollback.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="rollback-evidence.workspace">
        <RollbackEvidenceBoundaryBanner />
        <Surface testId="rollback-evidence.loading">
          <EmptyState title="Loading rollback evidence..." body="UI loading does not approve rollback, execute rollback, or continue workflow." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="rollback-evidence.workspace">
        <RollbackEvidenceBoundaryBanner />
        <Surface testId="rollback-evidence.error">
          <EmptyState title="Unable to load rollback evidence." body="No approval, rollback, source mutation, recovery, or workflow state changed." />
          <p className="state-muted">{rollbackEvidenceSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="rollback-evidence.workspace">
        <RollbackEvidenceBoundaryBanner />
        <Surface testId="rollback-evidence.empty">
          <EmptyState title="No rollback evidence selected." body="Missing rollback evidence does not permit rollback execution, retry, recovery, or workflow continuation." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="rollback-evidence.workspace">
      <RollbackEvidenceBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Rollback evidence</p>
            <h2>Rollback Evidence</h2>
            <p>
              This view displays supplied rollback evidence in one place. It does not create rollback plans, approve rollback, execute rollback, retry rollback, or make UI state authoritative.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="rollback-evidence.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims rollback satisfaction' : 'Supplied evidence does not claim rollback satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="rollback-evidence.identity">
            <div className="section-heading">
              <p className="eyebrow">Evidence identity</p>
              <h3>Evidence, not authority</h3>
            </div>
            <MetadataRow label="Rollback evidence id" value={rollbackEvidenceSafeText(evidence.rollbackEvidenceId)} />
            <MetadataRow label="Rollback evidence hash" value={rollbackEvidenceSafeText(evidence.rollbackEvidenceHash)} />
            <MetadataRow label="Rollback status" value={rollbackEvidenceSafeText(evidence.rollbackStatus)} />
            <MetadataRow label="Reviewed by" value={rollbackEvidenceSafeText(evidence.reviewedBy)} />
            <MetadataRow label="Reviewed" value={DateTimeDisplay.toLocalDisplay(rollbackEvidenceSafeText(evidence.reviewedAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(rollbackEvidenceSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="rollback-evidence.sourceApplyBinding">
            <div className="section-heading">
              <p className="eyebrow">Source apply receipt</p>
              <h3>Apply evidence binding</h3>
            </div>
            <MetadataRow label="Source apply receipt id" value={rollbackEvidenceSafeText(evidence.sourceApplyReceiptId)} />
            <MetadataRow label="Source apply receipt hash" value={rollbackEvidenceSafeText(evidence.sourceApplyReceiptHash)} />
            <MetadataRow label="Project id" value={rollbackEvidenceSafeText(evidence.projectId)} />
          </Surface>

          <Surface testId="rollback-evidence.planBinding">
            <div className="section-heading">
              <p className="eyebrow">Rollback plan</p>
              <h3>Plan binding</h3>
            </div>
            <MetadataRow label="Rollback plan id" value={rollbackEvidenceSafeText(evidence.rollbackPlanId)} />
            <MetadataRow label="Rollback plan hash" value={rollbackEvidenceSafeText(evidence.rollbackPlanHash)} />
            <MetadataRow label="Rollback plan present" value={<BooleanBadge value={evidence.rollbackPlanPresent} />} />
          </Surface>

          <Surface testId="rollback-evidence.supportBinding">
            <div className="section-heading">
              <p className="eyebrow">Rollback support receipt</p>
              <h3>Support evidence</h3>
            </div>
            <MetadataRow label="Rollback support receipt id" value={rollbackEvidenceSafeText(evidence.rollbackSupportReceiptId)} />
            <MetadataRow label="Rollback support receipt hash" value={rollbackEvidenceSafeText(evidence.rollbackSupportReceiptHash)} />
            <MetadataRow label="Rollback support receipt present" value={<BooleanBadge value={evidence.rollbackSupportReceiptPresent} />} />
          </Surface>

          <Surface testId="rollback-evidence.executionBinding">
            <div className="section-heading">
              <p className="eyebrow">Rollback execution receipt</p>
              <h3>Execution evidence, not execution by UI</h3>
            </div>
            <MetadataRow label="Rollback execution receipt id" value={rollbackEvidenceSafeText(evidence.rollbackExecutionReceiptId)} />
            <MetadataRow label="Rollback execution receipt hash" value={rollbackEvidenceSafeText(evidence.rollbackExecutionReceiptHash)} />
            <MetadataRow label="Rollback execution receipt present" value={<BooleanBadge value={evidence.rollbackExecutionReceiptPresent} />} />
          </Surface>

          <Surface testId="rollback-evidence.auditBinding">
            <div className="section-heading">
              <p className="eyebrow">Rollback audit report</p>
              <h3>Audit evidence</h3>
            </div>
            <MetadataRow label="Rollback audit report id" value={rollbackEvidenceSafeText(evidence.rollbackAuditReportId)} />
            <MetadataRow label="Rollback audit report hash" value={rollbackEvidenceSafeText(evidence.rollbackAuditReportHash)} />
            <MetadataRow label="Rollback audit report present" value={<BooleanBadge value={evidence.rollbackAuditReportPresent} />} />
            <MetadataRow label="Rollback audit consistent" value={<BooleanBadge value={evidence.rollbackAuditConsistent} />} />
          </Surface>

          <Surface testId="rollback-evidence.subjectBinding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Bound evidence</h3>
            </div>
            <MetadataRow label="Subject kind" value={rollbackEvidenceSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={rollbackEvidenceSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={rollbackEvidenceSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={rollbackEvidenceSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={rollbackEvidenceSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="rollback-evidence.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied rollback state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Rollback succeeded elsewhere" value={<BooleanBadge value={evidence.rollbackSucceeded} />} />
            <MetadataRow label="Rollback partial elsewhere" value={<BooleanBadge value={evidence.rollbackPartial} />} />
            <MetadataRow label="Rollback failed elsewhere" value={<BooleanBadge value={evidence.rollbackFailed} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="rollback-evidence.currentBadge">Supplied rollback evidence is available.</p> : null}
          </Surface>

          <Surface testId="rollback-evidence.affectedFiles">
            <div className="section-heading">
              <p className="eyebrow">Affected file/action summary</p>
              <h3>Summary only</h3>
            </div>
            <MetadataRow label="Affected file count" value={`${evidence.affectedFileCount}`} />
            {evidence.affectedFiles.length === 0 ? (
              <p data-testid="rollback-evidence.noAffectedFiles">No affected file/action summary supplied. Missing summary does not permit rollback execution.</p>
            ) : (
              <ul className="detail-list">
                {evidence.affectedFiles.map((file, index) => (
                  <li key={`${file.path}-${index}`}>
                    <strong>{rollbackEvidenceSafeText(file.action)}</strong>: {rollbackEvidenceSafeText(file.path)}
                    {' - '}{rollbackEvidenceSafeText(file.safeSummary)}
                    {file.beforeHash ? <> before {rollbackEvidenceSafeText(file.beforeHash)}</> : null}
                    {file.afterHash ? <> after {rollbackEvidenceSafeText(file.afterHash)}</> : null}
                  </li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="rollback-evidence.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="rollback-evidence.noEvidenceRefs">No evidence references supplied. Missing evidence does not permit rollback execution.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{rollbackEvidenceSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="rollback-evidence.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="rollback-evidence.incompleteWarning">
                Rollback evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : missingBoundaryMaxims ? 'boundaryMaxims' : missingRollbackParts ? 'rollback evidence binding' : 'invalid timestamp'}.
              </p>
            ) : null}
            {missingEvidenceRefs ? <p data-testid="rollback-evidence.missingEvidenceWarning">Rollback evidence has no evidence references and cannot permit rollback execution.</p> : null}
            {stale ? <p data-testid="rollback-evidence.staleWarning">Rollback evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="rollback-evidence.expiredWarning">Rollback evidence is expired. This UI will not renew evidence.</p> : null}
            {partial ? <p data-testid="rollback-evidence.partialWarning">Rollback evidence says rollback was partial elsewhere. This UI will not retry rollback or declare recovery complete.</p> : null}
            {failed ? <p data-testid="rollback-evidence.failureWarning">Rollback evidence says rollback failed elsewhere. This UI will not start recovery, source apply, rollback, or workflow continuation.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="rollback-evidence.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="rollback-evidence.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="rollback-evidence.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{rollbackEvidenceSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="rollback-evidence.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {rollbackEvidenceBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="rollback-evidence.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.rollbackEvidenceId, 'Rollback evidence id')}>
            Copy Rollback Evidence ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.rollbackEvidenceHash, 'Rollback evidence hash')}>
            Copy Rollback Evidence Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.rollbackPlanHash, 'Rollback plan hash')}>
            Copy Rollback Plan Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="rollback-evidence.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function RollbackEvidenceBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="rollback-evidence.boundaryBanner">
      <div className="approval-package-banner">
        <span>{rollbackEvidenceBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: RollbackEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.rollbackEvidenceId,
    evidence.rollbackEvidenceHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.sourceApplyReceiptId,
    evidence.sourceApplyReceiptHash,
    evidence.rollbackPlanId,
    evidence.rollbackPlanHash,
    evidence.rollbackSupportReceiptId,
    evidence.rollbackSupportReceiptHash,
    evidence.rollbackExecutionReceiptId,
    evidence.rollbackExecutionReceiptHash,
    evidence.rollbackAuditReportId,
    evidence.rollbackAuditReportHash,
    evidence.reviewedBy,
    evidence.rollbackStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.affectedFiles.flatMap((file) => [file.path, file.action, file.safeSummary, file.beforeHash, file.afterHash])
  ].some(containsRollbackEvidenceUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: RollbackEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.rollbackEvidenceId,
    evidence.rollbackEvidenceHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.sourceApplyReceiptId,
    evidence.sourceApplyReceiptHash,
    evidence.rollbackPlanId,
    evidence.rollbackPlanHash,
    evidence.rollbackSupportReceiptId,
    evidence.rollbackSupportReceiptHash,
    evidence.rollbackExecutionReceiptId,
    evidence.rollbackExecutionReceiptHash,
    evidence.rollbackAuditReportId,
    evidence.rollbackAuditReportHash,
    evidence.reviewedBy,
    evidence.rollbackStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.affectedFiles.flatMap((file) => [file.path, file.action, file.safeSummary, file.beforeHash, file.afterHash])
  ].some(containsRollbackEvidenceAuthorityClaim);
}
