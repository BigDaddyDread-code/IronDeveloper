import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsSourceApplyReviewAuthorityClaim,
  containsSourceApplyReviewUnsafeText,
  sourceApplyReviewBoundaryBanner,
  sourceApplyReviewBoundaryRules,
  sourceApplyReviewSafeText
} from './SourceApplyReviewBoundary';
import type { SourceApplyReviewEvidence, SourceApplyReviewPanelProps } from './SourceApplyReviewTypes';
import {
  hasInvalidSourceApplyReviewTimestamp,
  hasSourceApplyReviewAuthorityFlags,
  missingSourceApplyReviewFields,
  sourceApplyReviewDefaultDisplayState
} from './SourceApplyReviewTypes';

export function SourceApplyReviewPanel({ evidence = null, isLoading = false, errorMessage = null }: SourceApplyReviewPanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? sourceApplyReviewDefaultDisplayState;
  const missingFields = useMemo(() => missingSourceApplyReviewFields(evidence), [evidence]);
  const invalidTimestamp =
    hasInvalidSourceApplyReviewTimestamp(evidence?.reviewedAtUtc) ||
    hasInvalidSourceApplyReviewTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasSourceApplyReviewAuthorityFlags(evidence);
  const missingEvidenceRefs = evidence?.evidenceRefs.length === 0;
  const missingBoundaryMaxims = evidence?.boundaryMaxims.length === 0;
  const missingReviewParts = Boolean(evidence) && (!evidence?.patchArtifactPresent || !evidence?.dryRunReceiptPresent || !evidence?.requestBindingPresent);
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp || missingBoundaryMaxims || missingReviewParts;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.recordStored &&
    displayState.humanReviewRequired &&
    evidence?.patchArtifactSatisfied === true &&
    evidence?.dryRunReceiptSatisfied === true &&
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
    setCopyMessage(`${label} copied for inspection only. Copying source-apply review evidence does not apply source.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="source-apply-review.workspace">
        <SourceApplyReviewBoundaryBanner />
        <Surface testId="source-apply-review.loading">
          <EmptyState title="Loading source-apply review evidence..." body="UI loading does not approve or apply source." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="source-apply-review.workspace">
        <SourceApplyReviewBoundaryBanner />
        <Surface testId="source-apply-review.error">
          <EmptyState title="Unable to load source-apply review evidence." body="No approval, dry-run, source mutation, rollback, or workflow state changed." />
          <p className="state-muted">{sourceApplyReviewSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="source-apply-review.workspace">
        <SourceApplyReviewBoundaryBanner />
        <Surface testId="source-apply-review.empty">
          <EmptyState title="No source-apply review evidence selected." body="Missing review evidence does not permit source apply." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="source-apply-review.workspace">
      <SourceApplyReviewBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Source-apply review evidence</p>
            <h2>Source Apply Review Evidence</h2>
            <p>
              This view displays supplied source-apply review evidence in one place. It does not approve source apply, execute dry-run, apply source, or make UI state authoritative.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="source-apply-review.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims review satisfaction' : 'Supplied evidence does not claim review satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="source-apply-review.identity">
            <div className="section-heading">
              <p className="eyebrow">Review identity</p>
              <h3>Review, not authority</h3>
            </div>
            <MetadataRow label="Review id" value={sourceApplyReviewSafeText(evidence.reviewId)} />
            <MetadataRow label="Review hash" value={sourceApplyReviewSafeText(evidence.reviewHash)} />
            <MetadataRow label="Review status" value={sourceApplyReviewSafeText(evidence.sourceApplyReviewStatus)} />
            <MetadataRow label="Reviewed by" value={sourceApplyReviewSafeText(evidence.reviewedBy)} />
            <MetadataRow label="Reviewed" value={DateTimeDisplay.toLocalDisplay(sourceApplyReviewSafeText(evidence.reviewedAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(sourceApplyReviewSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="source-apply-review.requestBinding">
            <div className="section-heading">
              <p className="eyebrow">Source apply request</p>
              <h3>Request binding</h3>
            </div>
            <MetadataRow label="Source apply request id" value={sourceApplyReviewSafeText(evidence.sourceApplyRequestId)} />
            <MetadataRow label="Source apply request hash" value={sourceApplyReviewSafeText(evidence.sourceApplyRequestHash)} />
            <MetadataRow label="Request binding present" value={<BooleanBadge value={evidence.requestBindingPresent} />} />
            <MetadataRow label="Project id" value={sourceApplyReviewSafeText(evidence.projectId)} />
          </Surface>

          <Surface testId="source-apply-review.patchBinding">
            <div className="section-heading">
              <p className="eyebrow">Patch artifact</p>
              <h3>Patch evidence</h3>
            </div>
            <MetadataRow label="Patch artifact id" value={sourceApplyReviewSafeText(evidence.patchArtifactId)} />
            <MetadataRow label="Patch artifact hash" value={sourceApplyReviewSafeText(evidence.patchArtifactHash)} />
            <MetadataRow label="Patch artifact present" value={<BooleanBadge value={evidence.patchArtifactPresent} />} />
            <MetadataRow label="Patch artifact satisfied" value={<BooleanBadge value={evidence.patchArtifactSatisfied} />} />
          </Surface>

          <Surface testId="source-apply-review.dryRunBinding">
            <div className="section-heading">
              <p className="eyebrow">Dry-run receipt</p>
              <h3>Dry-run evidence</h3>
            </div>
            <MetadataRow label="Dry-run receipt id" value={sourceApplyReviewSafeText(evidence.dryRunReceiptId)} />
            <MetadataRow label="Dry-run receipt hash" value={sourceApplyReviewSafeText(evidence.dryRunReceiptHash)} />
            <MetadataRow label="Dry-run receipt present" value={<BooleanBadge value={evidence.dryRunReceiptPresent} />} />
            <MetadataRow label="Dry-run receipt satisfied" value={<BooleanBadge value={evidence.dryRunReceiptSatisfied} />} />
          </Surface>

          <Surface testId="source-apply-review.subjectBinding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Bound evidence</h3>
            </div>
            <MetadataRow label="Subject kind" value={sourceApplyReviewSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={sourceApplyReviewSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={sourceApplyReviewSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={sourceApplyReviewSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={sourceApplyReviewSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="source-apply-review.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied review state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="source-apply-review.currentBadge">Supplied source-apply review evidence is available.</p> : null}
          </Surface>

          <Surface testId="source-apply-review.plannedFiles">
            <div className="section-heading">
              <p className="eyebrow">Planned file/action summary</p>
              <h3>Summary only</h3>
            </div>
            <MetadataRow label="Planned change count" value={`${evidence.plannedChangeCount}`} />
            {evidence.plannedFileSummaries.length === 0 ? (
              <p data-testid="source-apply-review.noPlannedFiles">No planned file/action summary supplied. Missing summary does not permit source apply.</p>
            ) : (
              <ul className="detail-list">
                {evidence.plannedFileSummaries.map((file, index) => (
                  <li key={`${file.path}-${index}`}>
                    <strong>{sourceApplyReviewSafeText(file.action)}</strong>: {sourceApplyReviewSafeText(file.path)}
                    {file.previousPath ? <> from {sourceApplyReviewSafeText(file.previousPath)}</> : null}
                    {' - '}{sourceApplyReviewSafeText(file.safeSummary)}
                  </li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="source-apply-review.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="source-apply-review.noEvidenceRefs">No evidence references supplied. Missing evidence does not permit source apply.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{sourceApplyReviewSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="source-apply-review.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="source-apply-review.incompleteWarning">
                Source-apply review evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : missingBoundaryMaxims ? 'boundaryMaxims' : missingReviewParts ? 'review evidence binding' : 'invalid timestamp'}.
              </p>
            ) : null}
            {missingEvidenceRefs ? <p data-testid="source-apply-review.missingEvidenceWarning">Source-apply review evidence has no evidence references and cannot permit source apply.</p> : null}
            {stale ? <p data-testid="source-apply-review.staleWarning">Source-apply review evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="source-apply-review.expiredWarning">Source-apply review evidence is expired. This UI will not renew evidence.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="source-apply-review.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="source-apply-review.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="source-apply-review.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{sourceApplyReviewSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="source-apply-review.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {sourceApplyReviewBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="source-apply-review.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.reviewId, 'Review id')}>
            Copy Review ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.reviewHash, 'Review hash')}>
            Copy Review Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.sourceApplyRequestHash, 'Source apply request hash')}>
            Copy Source Apply Request Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="source-apply-review.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function SourceApplyReviewBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="source-apply-review.boundaryBanner">
      <div className="approval-package-banner">
        <span>{sourceApplyReviewBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: SourceApplyReviewEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.reviewId,
    evidence.reviewHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.sourceApplyRequestId,
    evidence.sourceApplyRequestHash,
    evidence.patchArtifactId,
    evidence.patchArtifactHash,
    evidence.dryRunReceiptId,
    evidence.dryRunReceiptHash,
    evidence.reviewedBy,
    evidence.sourceApplyReviewStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.plannedFileSummaries.flatMap((file) => [file.path, file.previousPath, file.action, file.safeSummary])
  ].some(containsSourceApplyReviewUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: SourceApplyReviewEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.reviewId,
    evidence.reviewHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.sourceApplyRequestId,
    evidence.sourceApplyRequestHash,
    evidence.patchArtifactId,
    evidence.patchArtifactHash,
    evidence.dryRunReceiptId,
    evidence.dryRunReceiptHash,
    evidence.reviewedBy,
    evidence.sourceApplyReviewStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.plannedFileSummaries.flatMap((file) => [file.path, file.previousPath, file.action, file.safeSummary])
  ].some(containsSourceApplyReviewAuthorityClaim);
}
