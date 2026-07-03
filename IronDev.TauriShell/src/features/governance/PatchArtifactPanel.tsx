import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsPatchArtifactAuthorityClaim,
  containsPatchArtifactUnsafeText,
  patchArtifactBoundaryBanner,
  patchArtifactBoundaryRules,
  patchArtifactSafeText
} from './PatchArtifactBoundary';
import type { PatchArtifactEvidence, PatchArtifactPanelProps } from './PatchArtifactTypes';
import {
  hasInvalidPatchArtifactTimestamp,
  hasPatchArtifactAuthorityFlags,
  missingPatchArtifactFields,
  patchArtifactDefaultDisplayState
} from './PatchArtifactTypes';

export function PatchArtifactPanel({ evidence = null, isLoading = false, errorMessage = null }: PatchArtifactPanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? patchArtifactDefaultDisplayState;
  const missingFields = useMemo(() => missingPatchArtifactFields(evidence), [evidence]);
  const invalidTimestamp =
    hasInvalidPatchArtifactTimestamp(evidence?.createdAtUtc) ||
    hasInvalidPatchArtifactTimestamp(evidence?.storedAtUtc) ||
    hasInvalidPatchArtifactTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasPatchArtifactAuthorityFlags(evidence);
  const missingEvidenceRefs = evidence?.evidenceRefs.length === 0;
  const missingBoundaryMaxims = evidence?.boundaryMaxims.length === 0;
  const rawPatchBodyRendered = evidence?.rawPatchBodyRendered !== false;
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp || missingBoundaryMaxims || rawPatchBodyRendered;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.recordStored &&
    displayState.humanReviewRequired &&
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
    setCopyMessage(`${label} copied for inspection only. Copying patch artifact evidence does not apply the patch.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="patch-artifact.workspace">
        <PatchArtifactBoundaryBanner />
        <Surface testId="patch-artifact.loading">
          <EmptyState title="Loading patch artifact evidence..." body="UI loading does not create patch artifacts, run dry-run, or apply source." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="patch-artifact.workspace">
        <PatchArtifactBoundaryBanner />
        <Surface testId="patch-artifact.error">
          <EmptyState title="Unable to load patch artifact evidence." body="No patch artifact, dry-run, approval, source mutation, rollback, or workflow state changed." />
          <p className="state-muted">{patchArtifactSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="patch-artifact.workspace">
        <PatchArtifactBoundaryBanner />
        <Surface testId="patch-artifact.empty">
          <EmptyState title="No patch artifact evidence selected." body="Missing patch artifact evidence does not permit dry-run or source apply." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="patch-artifact.workspace">
      <PatchArtifactBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Patch artifact evidence</p>
            <h2>Patch Artifact Evidence</h2>
            <p>
              This view displays supplied patch artifact evidence. It does not create or edit patch artifacts, execute dry-run, approve source apply, apply source, or make UI state authoritative.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="patch-artifact.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims artifact satisfaction' : 'Supplied evidence does not claim artifact satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="patch-artifact.identity">
            <div className="section-heading">
              <p className="eyebrow">Artifact identity</p>
              <h3>Evidence, not mutation</h3>
            </div>
            <MetadataRow label="Patch artifact id" value={patchArtifactSafeText(evidence.patchArtifactId)} />
            <MetadataRow label="Patch artifact hash" value={patchArtifactSafeText(evidence.patchArtifactHash)} />
            <MetadataRow label="Artifact status" value={patchArtifactSafeText(evidence.patchArtifactStatus)} />
            <MetadataRow label="Created by" value={patchArtifactSafeText(evidence.createdBy)} />
            <MetadataRow label="Created" value={DateTimeDisplay.toLocalDisplay(patchArtifactSafeText(evidence.createdAtUtc))} />
            <MetadataRow label="Stored" value={DateTimeDisplay.toLocalDisplay(patchArtifactSafeText(evidence.storedAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(patchArtifactSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="patch-artifact.sourceBinding">
            <div className="section-heading">
              <p className="eyebrow">Source binding</p>
              <h3>Source evidence</h3>
            </div>
            <MetadataRow label="Source kind" value={patchArtifactSafeText(evidence.sourceKind)} />
            <MetadataRow label="Source id" value={patchArtifactSafeText(evidence.sourceId)} />
            <MetadataRow label="Source hash" value={patchArtifactSafeText(evidence.sourceHash)} />
            <MetadataRow label="Project id" value={patchArtifactSafeText(evidence.projectId)} />
          </Surface>

          <Surface testId="patch-artifact.subjectBinding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Bound evidence</h3>
            </div>
            <MetadataRow label="Subject kind" value={patchArtifactSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={patchArtifactSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={patchArtifactSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={patchArtifactSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={patchArtifactSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="patch-artifact.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied artifact state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="patch-artifact.currentBadge">Supplied patch artifact evidence is available.</p> : null}
          </Surface>

          <Surface testId="patch-artifact.files">
            <div className="section-heading">
              <p className="eyebrow">File/action summary</p>
              <h3>Safe summaries only</h3>
            </div>
            <MetadataRow label="File count" value={`${evidence.fileCount}`} />
            {evidence.files.length === 0 ? (
              <p data-testid="patch-artifact.noFiles">No file/action summary supplied. Missing summary does not permit dry-run or source apply.</p>
            ) : (
              <ul className="detail-list">
                {evidence.files.map((file, index) => (
                  <li key={`${file.path}-${index}`}>
                    <strong>{patchArtifactSafeText(file.action)}</strong>: {patchArtifactSafeText(file.path)}
                    {file.previousPath ? <> from {patchArtifactSafeText(file.previousPath)}</> : null}
                    {' - '}{patchArtifactSafeText(file.safeSummary)}
                  </li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="patch-artifact.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="patch-artifact.noEvidenceRefs">No evidence references supplied. Missing evidence does not permit dry-run or source apply.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{patchArtifactSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="patch-artifact.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="patch-artifact.incompleteWarning">
                Patch artifact evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : missingBoundaryMaxims ? 'boundaryMaxims' : rawPatchBodyRendered ? 'rawPatchBodyRendered' : 'invalid timestamp'}.
              </p>
            ) : null}
            {missingEvidenceRefs ? <p data-testid="patch-artifact.missingEvidenceWarning">Patch artifact evidence has no evidence references and cannot permit dry-run or source apply.</p> : null}
            {evidence.rawPatchBodyPresent ? <p data-testid="patch-artifact.rawPatchWarning">Raw patch payload is present elsewhere and intentionally not rendered by this UI.</p> : null}
            {stale ? <p data-testid="patch-artifact.staleWarning">Patch artifact evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="patch-artifact.expiredWarning">Patch artifact evidence is expired. This UI will not renew evidence.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="patch-artifact.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="patch-artifact.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="patch-artifact.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{patchArtifactSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="patch-artifact.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {patchArtifactBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="patch-artifact.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.patchArtifactId, 'Patch artifact id')}>
            Copy Patch Artifact ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.patchArtifactHash, 'Patch artifact hash')}>
            Copy Patch Artifact Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.sourceHash, 'Source hash')}>
            Copy Source Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="patch-artifact.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function PatchArtifactBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="patch-artifact.boundaryBanner">
      <div className="approval-package-banner">
        <span>{patchArtifactBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: PatchArtifactEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.patchArtifactId,
    evidence.patchArtifactHash,
    evidence.patchArtifactStatus,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.createdBy,
    evidence.sourceKind,
    evidence.sourceId,
    evidence.sourceHash,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.files.flatMap((file) => [file.path, file.previousPath, file.action, file.fileHashBefore, file.fileHashAfter, file.safeSummary])
  ].some(containsPatchArtifactUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: PatchArtifactEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.patchArtifactId,
    evidence.patchArtifactHash,
    evidence.patchArtifactStatus,
    evidence.projectId,
    evidence.subjectKind,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowRunId,
    evidence.workflowStepId,
    evidence.createdBy,
    evidence.sourceKind,
    evidence.sourceId,
    evidence.sourceHash,
    ...evidence.evidenceRefs,
    ...evidence.warnings,
    ...evidence.boundaryMaxims,
    ...evidence.files.flatMap((file) => [file.path, file.previousPath, file.action, file.fileHashBefore, file.fileHashAfter, file.safeSummary])
  ].some(containsPatchArtifactAuthorityClaim);
}
