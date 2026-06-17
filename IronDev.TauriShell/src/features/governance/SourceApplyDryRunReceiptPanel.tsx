import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsSourceApplyDryRunReceiptAuthorityClaim,
  containsSourceApplyDryRunReceiptUnsafeText,
  sourceApplyDryRunReceiptBoundaryBanner,
  sourceApplyDryRunReceiptBoundaryRules,
  sourceApplyDryRunReceiptSafeText
} from './SourceApplyDryRunReceiptBoundary';
import type { SourceApplyDryRunReceiptEvidence, SourceApplyDryRunReceiptPanelProps } from './SourceApplyDryRunReceiptTypes';
import {
  hasInvalidSourceApplyDryRunReceiptTimestamp,
  hasSourceApplyDryRunReceiptAuthorityFlags,
  missingSourceApplyDryRunReceiptFields,
  sourceApplyDryRunReceiptDefaultDisplayState
} from './SourceApplyDryRunReceiptTypes';

export function SourceApplyDryRunReceiptPanel({ evidence = null, isLoading = false, errorMessage = null }: SourceApplyDryRunReceiptPanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? sourceApplyDryRunReceiptDefaultDisplayState;
  const missingFields = useMemo(() => missingSourceApplyDryRunReceiptFields(evidence), [evidence]);
  const invalidTimestamp =
    hasInvalidSourceApplyDryRunReceiptTimestamp(evidence?.dryRunStartedAtUtc) ||
    hasInvalidSourceApplyDryRunReceiptTimestamp(evidence?.dryRunCompletedAtUtc) ||
    hasInvalidSourceApplyDryRunReceiptTimestamp(evidence?.receiptStoredAtUtc) ||
    hasInvalidSourceApplyDryRunReceiptTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasSourceApplyDryRunReceiptAuthorityFlags(evidence);
  const missingEvidenceRefs = evidence?.evidenceRefs.length === 0;
  const missingBoundaryMaxims = evidence?.boundaryMaxims.length === 0;
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp || missingBoundaryMaxims;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.recordStored &&
    displayState.humanReviewRequired &&
    evidence?.validationPassed === true &&
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
    setCopyMessage(`${label} copied for inspection only. Copying dry-run receipt evidence does not apply source.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="dry-run-receipt.workspace">
        <SourceApplyDryRunReceiptBoundaryBanner />
        <Surface testId="dry-run-receipt.loading">
          <EmptyState title="Loading dry-run receipt evidence..." body="UI loading does not execute dry-run or apply source." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="dry-run-receipt.workspace">
        <SourceApplyDryRunReceiptBoundaryBanner />
        <Surface testId="dry-run-receipt.error">
          <EmptyState title="Unable to load dry-run receipt evidence." body="No dry-run, approval, source mutation, rollback, or workflow state changed." />
          <p className="state-muted">{sourceApplyDryRunReceiptSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="dry-run-receipt.workspace">
        <SourceApplyDryRunReceiptBoundaryBanner />
        <Surface testId="dry-run-receipt.empty">
          <EmptyState title="No dry-run receipt evidence selected." body="Missing dry-run receipt evidence does not permit source apply." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="dry-run-receipt.workspace">
      <SourceApplyDryRunReceiptBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Dry-run receipt evidence</p>
            <h2>Source Apply Dry-run Receipt Evidence</h2>
            <p>
              This view displays supplied source-apply dry-run receipt evidence. It does not execute dry-run, approve source apply, apply source, or make UI state authoritative.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="dry-run-receipt.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims receipt satisfaction' : 'Supplied evidence does not claim receipt satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="dry-run-receipt.identity">
            <div className="section-heading">
              <p className="eyebrow">Receipt identity</p>
              <h3>Record, not action</h3>
            </div>
            <MetadataRow label="Dry-run receipt id" value={sourceApplyDryRunReceiptSafeText(evidence.dryRunReceiptId)} />
            <MetadataRow label="Dry-run receipt hash" value={sourceApplyDryRunReceiptSafeText(evidence.dryRunReceiptHash)} />
            <MetadataRow label="Dry-run status" value={sourceApplyDryRunReceiptSafeText(evidence.dryRunStatus)} />
            <MetadataRow label="Validation passed" value={<BooleanBadge value={evidence.validationPassed} />} />
            <MetadataRow label="Stored" value={DateTimeDisplay.toLocalDisplay(sourceApplyDryRunReceiptSafeText(evidence.receiptStoredAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(sourceApplyDryRunReceiptSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="dry-run-receipt.requestBinding">
            <div className="section-heading">
              <p className="eyebrow">Source apply request binding</p>
              <h3>Request evidence, not source apply</h3>
            </div>
            <MetadataRow label="Source apply request id" value={sourceApplyDryRunReceiptSafeText(evidence.sourceApplyRequestId)} />
            <MetadataRow label="Source apply request hash" value={sourceApplyDryRunReceiptSafeText(evidence.sourceApplyRequestHash)} />
            <MetadataRow label="Project id" value={sourceApplyDryRunReceiptSafeText(evidence.projectId)} />
            <MetadataRow label="Requested by" value={sourceApplyDryRunReceiptSafeText(evidence.requestedBy)} />
          </Surface>

          <Surface testId="dry-run-receipt.subjectBinding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Bound evidence</h3>
            </div>
            <MetadataRow label="Subject kind" value={sourceApplyDryRunReceiptSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={sourceApplyDryRunReceiptSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={sourceApplyDryRunReceiptSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={sourceApplyDryRunReceiptSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={sourceApplyDryRunReceiptSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="dry-run-receipt.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied receipt state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="dry-run-receipt.currentBadge">Supplied dry-run receipt evidence is available.</p> : null}
          </Surface>

          <Surface testId="dry-run-receipt.plannedFiles">
            <div className="section-heading">
              <p className="eyebrow">Planned file/action summary</p>
              <h3>Summary only</h3>
            </div>
            <MetadataRow label="Planned change count" value={`${evidence.plannedChangeCount}`} />
            {evidence.plannedFiles.length === 0 ? (
              <p data-testid="dry-run-receipt.noPlannedFiles">No planned file/action summary supplied. Missing summary does not permit source apply.</p>
            ) : (
              <ul className="detail-list">
                {evidence.plannedFiles.map((file, index) => (
                  <li key={`${file.path}-${index}`}>
                    <strong>{sourceApplyDryRunReceiptSafeText(file.action)}</strong>: {sourceApplyDryRunReceiptSafeText(file.path)}
                    {file.previousPath ? <> from {sourceApplyDryRunReceiptSafeText(file.previousPath)}</> : null}
                    {' - '}{sourceApplyDryRunReceiptSafeText(file.safeSummary)}
                  </li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="dry-run-receipt.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="dry-run-receipt.noEvidenceRefs">No evidence references supplied. Missing evidence does not permit source apply.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{sourceApplyDryRunReceiptSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="dry-run-receipt.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="dry-run-receipt.incompleteWarning">
                Dry-run receipt evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : missingBoundaryMaxims ? 'boundaryMaxims' : 'invalid timestamp'}.
              </p>
            ) : null}
            {missingEvidenceRefs ? <p data-testid="dry-run-receipt.missingEvidenceWarning">Dry-run receipt evidence has no evidence references and cannot permit source apply.</p> : null}
            {stale ? <p data-testid="dry-run-receipt.staleWarning">Dry-run receipt evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="dry-run-receipt.expiredWarning">Dry-run receipt evidence is expired. This UI will not renew evidence.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="dry-run-receipt.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="dry-run-receipt.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="dry-run-receipt.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{sourceApplyDryRunReceiptSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="dry-run-receipt.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {sourceApplyDryRunReceiptBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="dry-run-receipt.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.dryRunReceiptId, 'Dry-run receipt id')}>
            Copy Dry-run Receipt ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.dryRunReceiptHash, 'Dry-run receipt hash')}>
            Copy Dry-run Receipt Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.sourceApplyRequestHash, 'Source apply request hash')}>
            Copy Source Apply Request Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="dry-run-receipt.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function SourceApplyDryRunReceiptBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="dry-run-receipt.boundaryBanner">
      <div className="approval-package-banner">
        <span>{sourceApplyDryRunReceiptBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: SourceApplyDryRunReceiptEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.dryRunReceiptId,
    evidence.dryRunReceiptHash,
    evidence.sourceApplyRequestId,
    evidence.sourceApplyRequestHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.requestedBy,
    evidence.dryRunStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.plannedFiles.flatMap((file) => [file.path, file.previousPath, file.action, file.fileHashBefore, file.fileHashAfter, file.safeSummary])
  ].some(containsSourceApplyDryRunReceiptUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: SourceApplyDryRunReceiptEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.dryRunReceiptId,
    evidence.dryRunReceiptHash,
    evidence.sourceApplyRequestId,
    evidence.sourceApplyRequestHash,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.requestedBy,
    evidence.dryRunStatus,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.plannedFiles.flatMap((file) => [file.path, file.previousPath, file.action, file.fileHashBefore, file.fileHashAfter, file.safeSummary])
  ].some(containsSourceApplyDryRunReceiptAuthorityClaim);
}
