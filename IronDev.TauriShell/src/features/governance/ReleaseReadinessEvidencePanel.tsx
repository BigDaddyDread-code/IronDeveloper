import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsReleaseReadinessEvidenceAuthorityClaim,
  containsReleaseReadinessEvidenceUnsafeText,
  releaseReadinessEvidenceBoundaryBanner,
  releaseReadinessEvidenceBoundaryRules,
  releaseReadinessEvidenceSafeText
} from './ReleaseReadinessEvidenceBoundary';
import type { ReleaseReadinessEvidence, ReleaseReadinessEvidencePanelProps } from './ReleaseReadinessEvidenceTypes';
import {
  hasInvalidReleaseReadinessEvidenceTimestamp,
  hasReleaseReadinessEvidenceAuthorityFlags,
  missingReleaseReadinessEvidenceFields,
  releaseReadinessEvidenceDefaultDisplayState
} from './ReleaseReadinessEvidenceTypes';

export function ReleaseReadinessEvidencePanel({ evidence = null, isLoading = false, errorMessage = null }: ReleaseReadinessEvidencePanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? releaseReadinessEvidenceDefaultDisplayState;
  const missingFields = useMemo(() => missingReleaseReadinessEvidenceFields(evidence), [evidence]);
  const invalidTimestamp =
    hasInvalidReleaseReadinessEvidenceTimestamp(evidence?.reviewedAtUtc) ||
    hasInvalidReleaseReadinessEvidenceTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasReleaseReadinessEvidenceAuthorityFlags(evidence);
  const missingEvidenceRefs = evidence?.evidenceRefs.length === 0;
  const missingBoundaryMaxims = evidence?.boundaryMaxims.length === 0;
  const missingReadinessParts = Boolean(evidence) && (
    !evidence?.releaseReadinessReportPresent ||
    !evidence?.approvalEvidencePresent ||
    !evidence?.policyEvidencePresent ||
    !evidence?.sourceApplyEvidencePresent ||
    !evidence?.workflowContinuationEvidencePresent
  );
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp || missingBoundaryMaxims || missingReadinessParts;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const blocked = evidence?.releaseBlocked === true;
  const failed = evidence?.releaseFailed === true;
  const partial = evidence?.releasePartial === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.recordStored &&
    displayState.humanReviewRequired &&
    evidence?.releaseReadinessReportPresent === true &&
    evidence?.releaseReadinessReportSatisfied === true &&
    evidence?.approvalEvidencePresent === true &&
    evidence?.policyEvidencePresent === true &&
    evidence?.sourceApplyEvidencePresent === true &&
    evidence?.workflowContinuationEvidencePresent === true &&
    !incomplete &&
    !missingEvidenceRefs &&
    !stale &&
    !expired &&
    !blocked &&
    !failed &&
    !partial &&
    !unsafeTextDetected &&
    !authorityClaimDetected &&
    !authorityFlagsDetected;

  const copyValue = useCallback((value: string | undefined, label: string) => {
    if (!value?.trim()) {
      setCopyMessage(`${label} is missing. Nothing was copied.`);
      return;
    }

    void navigator.clipboard?.writeText(value).catch(() => undefined);
    setCopyMessage(`${label} copied for inspection only. Copying release readiness evidence does not approve release.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="release-readiness-evidence.workspace">
        <ReleaseReadinessEvidenceBoundaryBanner />
        <Surface testId="release-readiness-evidence.loading">
          <EmptyState title="Loading release readiness evidence..." body="UI loading does not decide readiness, approve release, or execute release." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="release-readiness-evidence.workspace">
        <ReleaseReadinessEvidenceBoundaryBanner />
        <Surface testId="release-readiness-evidence.error">
          <EmptyState title="Unable to load release readiness evidence." body="No release approval, deployment approval, merge approval, execution, recovery, or workflow state changed." />
          <p className="state-muted">{releaseReadinessEvidenceSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="release-readiness-evidence.workspace">
        <ReleaseReadinessEvidenceBoundaryBanner />
        <Surface testId="release-readiness-evidence.empty">
          <EmptyState title="No release readiness evidence selected." body="Missing release readiness evidence does not approve release, deployment, merge, or execution." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="release-readiness-evidence.workspace">
      <ReleaseReadinessEvidenceBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Release readiness evidence</p>
            <h2>Release Readiness Evidence</h2>
            <p>
              This view displays supplied release readiness evidence in one place. It does not decide readiness, approve release, approve deployment, approve merge, or execute release.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="release-readiness-evidence.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims readiness satisfaction' : 'Supplied evidence does not claim readiness satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="release-readiness-evidence.identity">
            <div className="section-heading">
              <p className="eyebrow">Evidence identity</p>
              <h3>Evidence, not release approval</h3>
            </div>
            <MetadataRow label="Release readiness evidence id" value={releaseReadinessEvidenceSafeText(evidence.releaseReadinessEvidenceId)} />
            <MetadataRow label="Release readiness evidence hash" value={releaseReadinessEvidenceSafeText(evidence.releaseReadinessEvidenceHash)} />
            <MetadataRow label="Readiness status" value={releaseReadinessEvidenceSafeText(evidence.readinessStatus)} />
            <MetadataRow label="Reviewed by" value={releaseReadinessEvidenceSafeText(evidence.reviewedBy)} />
            <MetadataRow label="Reviewed" value={DateTimeDisplay.toLocalDisplay(releaseReadinessEvidenceSafeText(evidence.reviewedAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(releaseReadinessEvidenceSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="release-readiness-evidence.reportBinding">
            <div className="section-heading">
              <p className="eyebrow">Release readiness report</p>
              <h3>Report binding</h3>
            </div>
            <MetadataRow label="Release readiness report id" value={releaseReadinessEvidenceSafeText(evidence.releaseReadinessReportId)} />
            <MetadataRow label="Release readiness report hash" value={releaseReadinessEvidenceSafeText(evidence.releaseReadinessReportHash)} />
            <MetadataRow label="Report present" value={<BooleanBadge value={evidence.releaseReadinessReportPresent} />} />
            <MetadataRow label="Report satisfied" value={<BooleanBadge value={evidence.releaseReadinessReportSatisfied} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.decisionBinding">
            <div className="section-heading">
              <p className="eyebrow">Release readiness decision record</p>
              <h3>Decision evidence, not release execution</h3>
            </div>
            <MetadataRow label="Release readiness decision record id" value={releaseReadinessEvidenceSafeText(evidence.releaseReadinessDecisionRecordId)} />
            <MetadataRow label="Release readiness decision record hash" value={releaseReadinessEvidenceSafeText(evidence.releaseReadinessDecisionRecordHash)} />
            <MetadataRow label="Decision record present" value={<BooleanBadge value={evidence.releaseReadinessDecisionPresent} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.approvalBinding">
            <div className="section-heading">
              <p className="eyebrow">Accepted approval</p>
              <h3>Approval evidence binding</h3>
            </div>
            <MetadataRow label="Accepted approval id" value={releaseReadinessEvidenceSafeText(evidence.acceptedApprovalId)} />
            <MetadataRow label="Accepted approval hash" value={releaseReadinessEvidenceSafeText(evidence.acceptedApprovalHash)} />
            <MetadataRow label="Approval evidence present" value={<BooleanBadge value={evidence.approvalEvidencePresent} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.policyBinding">
            <div className="section-heading">
              <p className="eyebrow">Policy satisfaction</p>
              <h3>Policy evidence binding</h3>
            </div>
            <MetadataRow label="Policy satisfaction id" value={releaseReadinessEvidenceSafeText(evidence.policySatisfactionId)} />
            <MetadataRow label="Policy satisfaction hash" value={releaseReadinessEvidenceSafeText(evidence.policySatisfactionHash)} />
            <MetadataRow label="Policy evidence present" value={<BooleanBadge value={evidence.policyEvidencePresent} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.sourceApplyBinding">
            <div className="section-heading">
              <p className="eyebrow">Source apply review</p>
              <h3>Source-apply evidence binding</h3>
            </div>
            <MetadataRow label="Source apply review id" value={releaseReadinessEvidenceSafeText(evidence.sourceApplyReviewId)} />
            <MetadataRow label="Source apply review hash" value={releaseReadinessEvidenceSafeText(evidence.sourceApplyReviewHash)} />
            <MetadataRow label="Source apply evidence present" value={<BooleanBadge value={evidence.sourceApplyEvidencePresent} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.rollbackBinding">
            <div className="section-heading">
              <p className="eyebrow">Rollback evidence</p>
              <h3>Optional rollback binding</h3>
            </div>
            <MetadataRow label="Rollback evidence id" value={releaseReadinessEvidenceSafeText(evidence.rollbackEvidenceId)} />
            <MetadataRow label="Rollback evidence hash" value={releaseReadinessEvidenceSafeText(evidence.rollbackEvidenceHash)} />
            <MetadataRow label="Rollback evidence present" value={<BooleanBadge value={evidence.rollbackEvidencePresent} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.workflowContinuationBinding">
            <div className="section-heading">
              <p className="eyebrow">Workflow continuation evidence</p>
              <h3>Continuation evidence binding</h3>
            </div>
            <MetadataRow label="Workflow continuation evidence id" value={releaseReadinessEvidenceSafeText(evidence.workflowContinuationEvidenceId)} />
            <MetadataRow label="Workflow continuation evidence hash" value={releaseReadinessEvidenceSafeText(evidence.workflowContinuationEvidenceHash)} />
            <MetadataRow label="Workflow continuation evidence present" value={<BooleanBadge value={evidence.workflowContinuationEvidencePresent} />} />
          </Surface>

          <Surface testId="release-readiness-evidence.subjectBinding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Bound evidence</h3>
            </div>
            <MetadataRow label="Subject kind" value={releaseReadinessEvidenceSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={releaseReadinessEvidenceSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={releaseReadinessEvidenceSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={releaseReadinessEvidenceSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={releaseReadinessEvidenceSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="release-readiness-evidence.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied readiness state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Release ready claimed" value={<BooleanBadge value={evidence.releaseReadyClaimed} />} />
            <MetadataRow label="Release blocked" value={<BooleanBadge value={evidence.releaseBlocked} />} />
            <MetadataRow label="Release failed" value={<BooleanBadge value={evidence.releaseFailed} />} />
            <MetadataRow label="Release partial" value={<BooleanBadge value={evidence.releasePartial} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="release-readiness-evidence.currentBadge">Supplied release readiness evidence is available.</p> : null}
          </Surface>

          <Surface testId="release-readiness-evidence.findings">
            <div className="section-heading">
              <p className="eyebrow">Findings</p>
              <h3>Review findings only</h3>
            </div>
            {evidence.findings.length === 0 ? (
              <p data-testid="release-readiness-evidence.noFindings">No findings supplied. Missing findings do not approve release.</p>
            ) : (
              <ul className="detail-list">
                {evidence.findings.map((finding, index) => (
                  <li key={`${finding.code}-${index}`}>
                    <strong>{releaseReadinessEvidenceSafeText(finding.severity)}</strong>: {releaseReadinessEvidenceSafeText(finding.code)}
                    {' / '}{releaseReadinessEvidenceSafeText(finding.field)}
                    {' - '}{releaseReadinessEvidenceSafeText(finding.safeSummary)}
                  </li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="release-readiness-evidence.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="release-readiness-evidence.noEvidenceRefs">No evidence references supplied. Missing evidence does not approve release.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{releaseReadinessEvidenceSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="release-readiness-evidence.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="release-readiness-evidence.incompleteWarning">
                Release readiness evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : missingBoundaryMaxims ? 'boundaryMaxims' : missingReadinessParts ? 'release readiness evidence binding' : 'invalid timestamp'}.
              </p>
            ) : null}
            {missingEvidenceRefs ? <p data-testid="release-readiness-evidence.missingEvidenceWarning">Release readiness evidence has no evidence references and cannot approve release.</p> : null}
            {stale ? <p data-testid="release-readiness-evidence.staleWarning">Release readiness evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="release-readiness-evidence.expiredWarning">Release readiness evidence is expired. This UI will not renew evidence.</p> : null}
            {blocked ? <p data-testid="release-readiness-evidence.blockedWarning">Release readiness evidence says release is blocked. This UI will not retry, recover, approve, deploy, merge, execute, or continue workflow.</p> : null}
            {failed ? <p data-testid="release-readiness-evidence.failureWarning">Release readiness evidence says release failed. This UI will not retry, recover, approve, deploy, merge, execute, or continue workflow.</p> : null}
            {partial ? <p data-testid="release-readiness-evidence.partialWarning">Release readiness evidence is partial. This UI will not normalize partial evidence into release readiness.</p> : null}
            {evidence.releaseReadyClaimed ? <p data-testid="release-readiness-evidence.readyClaim">Release-ready claim is displayed as supplied evidence only, not approval.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="release-readiness-evidence.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="release-readiness-evidence.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="release-readiness-evidence.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{releaseReadinessEvidenceSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="release-readiness-evidence.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {releaseReadinessEvidenceBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="release-readiness-evidence.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.releaseReadinessEvidenceId, 'Release readiness evidence id')}>
            Copy Release Readiness Evidence ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.releaseReadinessEvidenceHash, 'Release readiness evidence hash')}>
            Copy Release Readiness Evidence Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.releaseReadinessReportHash, 'Release readiness report hash')}>
            Copy Release Readiness Report Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="release-readiness-evidence.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function ReleaseReadinessEvidenceBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="release-readiness-evidence.boundaryBanner">
      <div className="approval-package-banner">
        <span>{releaseReadinessEvidenceBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: ReleaseReadinessEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.releaseReadinessEvidenceId,
    evidence.releaseReadinessEvidenceHash,
    evidence.releaseReadinessReportId,
    evidence.releaseReadinessReportHash,
    evidence.releaseReadinessDecisionRecordId,
    evidence.releaseReadinessDecisionRecordHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.acceptedApprovalId,
    evidence.acceptedApprovalHash,
    evidence.policySatisfactionId,
    evidence.policySatisfactionHash,
    evidence.sourceApplyReviewId,
    evidence.sourceApplyReviewHash,
    evidence.rollbackEvidenceId,
    evidence.rollbackEvidenceHash,
    evidence.workflowContinuationEvidenceId,
    evidence.workflowContinuationEvidenceHash,
    evidence.reviewedBy,
    evidence.readinessStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.findings.flatMap((finding) => [finding.code, finding.severity, finding.field, finding.safeSummary])
  ].some(containsReleaseReadinessEvidenceUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: ReleaseReadinessEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.releaseReadinessEvidenceId,
    evidence.releaseReadinessEvidenceHash,
    evidence.releaseReadinessReportId,
    evidence.releaseReadinessReportHash,
    evidence.releaseReadinessDecisionRecordId,
    evidence.releaseReadinessDecisionRecordHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.acceptedApprovalId,
    evidence.acceptedApprovalHash,
    evidence.policySatisfactionId,
    evidence.policySatisfactionHash,
    evidence.sourceApplyReviewId,
    evidence.sourceApplyReviewHash,
    evidence.rollbackEvidenceId,
    evidence.rollbackEvidenceHash,
    evidence.workflowContinuationEvidenceId,
    evidence.workflowContinuationEvidenceHash,
    evidence.reviewedBy,
    evidence.readinessStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.findings.flatMap((finding) => [finding.code, finding.severity, finding.field, finding.safeSummary])
  ].some(containsReleaseReadinessEvidenceAuthorityClaim);
}
