import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  containsPolicySatisfactionAuthorityClaim,
  containsPolicySatisfactionUnsafeText,
  policySatisfactionBoundaryBanner,
  policySatisfactionBoundaryRules,
  policySatisfactionSafeText
} from './PolicySatisfactionBoundary';
import type { PolicySatisfactionEvidence, PolicySatisfactionPanelProps } from './PolicySatisfactionTypes';
import {
  hasInvalidPolicySatisfactionTimestamp,
  hasPolicySatisfactionAuthorityFlags,
  missingPolicySatisfactionFields,
  policySatisfactionDefaultDisplayState
} from './PolicySatisfactionTypes';

export function PolicySatisfactionPanel({ evidence = null, isLoading = false, errorMessage = null }: PolicySatisfactionPanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const displayState = evidence?.displayState ?? policySatisfactionDefaultDisplayState;
  const missingFields = useMemo(() => missingPolicySatisfactionFields(evidence), [evidence]);
  const invalidTimestamp = hasInvalidPolicySatisfactionTimestamp(evidence?.evaluatedAtUtc) || hasInvalidPolicySatisfactionTimestamp(evidence?.expiresAtUtc);
  const unsafeTextDetected = useMemo(() => evidenceContainsUnsafeText(evidence), [evidence]);
  const authorityClaimDetected = useMemo(() => evidenceContainsAuthorityClaim(evidence), [evidence]);
  const authorityFlagsDetected = hasPolicySatisfactionAuthorityFlags(evidence);
  const incomplete = Boolean(evidence?.incomplete) || missingFields.length > 0 || invalidTimestamp;
  const stale = evidence?.stale === true;
  const expired = evidence?.expired === true;
  const currentEvidence =
    Boolean(evidence) &&
    displayState.evidencePresent &&
    displayState.evidenceSatisfied &&
    displayState.humanReviewRequired &&
    !incomplete &&
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
    setCopyMessage(`${label} copied for inspection only. Copying evidence does not satisfy policy.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="policy-satisfaction.workspace">
        <PolicySatisfactionBoundaryBanner />
        <Surface testId="policy-satisfaction.loading">
          <EmptyState title="Loading policy satisfaction evidence..." body="UI loading does not satisfy policy." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="policy-satisfaction.workspace">
        <PolicySatisfactionBoundaryBanner />
        <Surface testId="policy-satisfaction.error">
          <EmptyState title="Unable to load policy satisfaction evidence." body="No approval, execution, or workflow state changed." />
          <p className="state-muted">{policySatisfactionSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="policy-satisfaction.workspace">
        <PolicySatisfactionBoundaryBanner />
        <Surface testId="policy-satisfaction.empty">
          <EmptyState title="No policy satisfaction evidence selected." body="Missing evidence does not satisfy policy." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="policy-satisfaction.workspace">
      <PolicySatisfactionBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Policy satisfaction evidence</p>
            <h2>Policy Satisfaction Evidence</h2>
            <p>
              This view displays supplied policy satisfaction evidence. It does not evaluate policy, grant authority, or make UI state authoritative.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="policy-satisfaction.statusBanner">
            <span>{displayState.evidencePresent ? 'Evidence present' : 'Evidence missing'}</span>
            <span>{displayState.evidenceSatisfied ? 'Supplied evidence claims satisfaction' : 'Supplied evidence does not claim satisfaction'}</span>
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="policy-satisfaction.identity">
            <div className="section-heading">
              <p className="eyebrow">Policy identity</p>
              <h3>Evidence, not authority</h3>
            </div>
            <MetadataRow label="Policy id" value={policySatisfactionSafeText(evidence.policyId)} />
            <MetadataRow label="Policy name" value={policySatisfactionSafeText(evidence.policyName)} />
            <MetadataRow label="Policy version" value={policySatisfactionSafeText(evidence.policyVersion)} />
            <MetadataRow label="Evaluated" value={DateTimeDisplay.toLocalDisplay(policySatisfactionSafeText(evidence.evaluatedAtUtc))} />
            <MetadataRow label="Expires" value={evidence.expiresAtUtc ? DateTimeDisplay.toLocalDisplay(policySatisfactionSafeText(evidence.expiresAtUtc)) : 'No expiry supplied'} />
          </Surface>

          <Surface testId="policy-satisfaction.binding">
            <div className="section-heading">
              <p className="eyebrow">Binding</p>
              <h3>Subject, workflow, and approval references</h3>
            </div>
            <MetadataRow label="Subject id" value={policySatisfactionSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={policySatisfactionSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow id" value={policySatisfactionSafeText(evidence.workflowId)} />
            <MetadataRow label="Approval id" value={policySatisfactionSafeText(evidence.approvalId, 'No approval id supplied')} />
            <MetadataRow label="Approval hash" value={policySatisfactionSafeText(evidence.approvalHash, 'No approval hash supplied')} />
          </Surface>

          <Surface testId="policy-satisfaction.state">
            <div className="section-heading">
              <p className="eyebrow">Display state</p>
              <h3>Supplied evidence state</h3>
            </div>
            <MetadataRow label="Evidence present" value={<BooleanBadge value={displayState.evidencePresent} />} />
            <MetadataRow label="Evidence satisfied" value={<BooleanBadge value={displayState.evidenceSatisfied} />} />
            <MetadataRow label="Record stored" value={<BooleanBadge value={displayState.recordStored} />} />
            <MetadataRow label="Human review" value={<StatusBadge status="warning">{displayState.humanReviewRequired ? 'Human review required' : 'Human review required flag missing'}</StatusBadge>} />
            {currentEvidence ? <p data-testid="policy-satisfaction.currentBadge">Supplied policy satisfaction evidence is available.</p> : null}
          </Surface>

          <Surface testId="policy-satisfaction.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceRefs.length === 0 ? (
              <p data-testid="policy-satisfaction.noEvidenceRefs">No evidence references supplied. Missing evidence does not satisfy policy.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceRefs.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{policySatisfactionSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="policy-satisfaction.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Blocked and incomplete states</h3>
            </div>
            {incomplete ? (
              <p data-testid="policy-satisfaction.incompleteWarning">
                Policy satisfaction evidence is incomplete or invalid. Missing: {missingFields.length ? missingFields.join(', ') : 'invalid timestamp'}.
              </p>
            ) : null}
            {stale ? <p data-testid="policy-satisfaction.staleWarning">Policy satisfaction evidence is stale. This UI will not refresh authority.</p> : null}
            {expired ? <p data-testid="policy-satisfaction.expiredWarning">Policy satisfaction evidence is expired. This UI will not renew evidence.</p> : null}
            {unsafeTextDetected || evidence.unsafeMaterialDetected ? (
              <p data-testid="policy-satisfaction.unsafeWarning">Unsafe or private material was detected and is not rendered as authority.</p>
            ) : null}
            {authorityClaimDetected || evidence.authorityClaimsDetected || authorityFlagsDetected ? (
              <p data-testid="policy-satisfaction.authorityWarning">Authority claims were detected and are treated as warnings, not authority.</p>
            ) : null}
            {displayState.humanReviewRequired ? null : <p data-testid="policy-satisfaction.humanReviewWarning">Human review required flag is missing; this view cannot treat evidence as current.</p>}
            {evidence.warnings.length ? (
              <ul className="detail-list">
                {evidence.warnings.map((warning, index) => (
                  <li key={`${warning}-${index}`}>{policySatisfactionSafeText(warning)}</li>
                ))}
              </ul>
            ) : null}
          </Surface>

          <Surface testId="policy-satisfaction.boundaryRules">
            <div className="section-heading">
              <p className="eyebrow">Boundary</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {policySatisfactionBoundaryRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="policy-satisfaction.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.policyId, 'Policy id')}>
            Copy Policy ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.subjectHash, 'Subject hash')}>
            Copy Subject Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.approvalHash, 'Approval hash')}>
            Copy Approval Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceRefs.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="policy-satisfaction.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function PolicySatisfactionBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="policy-satisfaction.boundaryBanner">
      <div className="approval-package-banner">
        <span>{policySatisfactionBoundaryBanner}</span>
      </div>
    </section>
  );
}

function BooleanBadge({ value }: { value: boolean }) {
  return <StatusBadge status={value ? 'info' : 'warning'}>{value ? 'Supplied true' : 'Supplied false'}</StatusBadge>;
}

function evidenceContainsUnsafeText(evidence: PolicySatisfactionEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.policyId,
    evidence.policyName,
    evidence.policyVersion,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowId,
    evidence.approvalId,
    evidence.approvalHash,
    ...evidence.evidenceRefs,
    ...evidence.warnings
  ].some(containsPolicySatisfactionUnsafeText);
}

function evidenceContainsAuthorityClaim(evidence: PolicySatisfactionEvidence | null | undefined) {
  if (!evidence) {
    return false;
  }

  return [
    evidence.policyId,
    evidence.policyName,
    evidence.policyVersion,
    evidence.subjectId,
    evidence.subjectHash,
    evidence.workflowId,
    evidence.approvalId,
    evidence.approvalHash,
    ...evidence.evidenceRefs,
    ...evidence.warnings
  ].some(containsPolicySatisfactionAuthorityClaim);
}
