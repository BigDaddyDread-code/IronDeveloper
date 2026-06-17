import { useCallback, useMemo, useState } from 'react';
import { MetadataRow } from '../../components/MetadataRow';
import { EmptyState } from '../../design-system/EmptyState';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import { DateTimeDisplay } from '../../utils/dateTimeDisplay';
import {
  acceptedApprovalBoundaryBanner,
  acceptedApprovalBoundaryMaxims,
  acceptedApprovalHasAuthorityFlag,
  acceptedApprovalSafeText
} from './AcceptedApprovalBoundary';
import type { AcceptedApprovalEvidenceViewModel, AcceptedApprovalPanelProps } from './AcceptedApprovalTypes';
import { missingAcceptedApprovalFields } from './AcceptedApprovalTypes';

export function AcceptedApprovalPanel({ evidence = null, isLoading = false, errorMessage = null }: AcceptedApprovalPanelProps) {
  const [copyMessage, setCopyMessage] = useState('');
  const missingFields = useMemo(() => missingAcceptedApprovalFields(evidence), [evidence]);
  const incomplete = missingFields.length > 0;
  const stale = evidence?.isStale === true;
  const expired = evidence?.isExpired === true;
  const unsafeFlags = acceptedApprovalHasAuthorityFlag(evidence);
  const showCurrentEvidence = Boolean(evidence && !incomplete && !stale && !expired && !unsafeFlags);

  const copyValue = useCallback((value: string | undefined, label: string) => {
    if (!value?.trim()) {
      setCopyMessage(`${label} is missing. Nothing was copied.`);
      return;
    }

    void navigator.clipboard?.writeText(value).catch(() => undefined);
    setCopyMessage(`${label} copied for inspection only. Copying evidence does not create approval.`);
  }, []);

  if (isLoading) {
    return (
      <main className="approval-package-workspace" data-testid="accepted-approvals.workspace">
        <AcceptedApprovalBoundaryBanner />
        <Surface testId="accepted-approvals.loading">
          <EmptyState title="Loading accepted approval evidence..." body="No approval state changed." />
        </Surface>
      </main>
    );
  }

  if (errorMessage) {
    return (
      <main className="approval-package-workspace" data-testid="accepted-approvals.workspace">
        <AcceptedApprovalBoundaryBanner />
        <Surface testId="accepted-approvals.error">
          <EmptyState title="Unable to load accepted approval evidence." body="No approval state changed." />
          <p className="state-muted">{acceptedApprovalSafeText(errorMessage)}</p>
        </Surface>
      </main>
    );
  }

  if (!evidence) {
    return (
      <main className="approval-package-workspace" data-testid="accepted-approvals.workspace">
        <AcceptedApprovalBoundaryBanner />
        <Surface testId="accepted-approvals.empty">
          <EmptyState title="No accepted approval evidence selected." body="No authority is granted by this view." />
        </Surface>
      </main>
    );
  }

  return (
    <main className="approval-package-workspace" data-testid="accepted-approvals.workspace">
      <AcceptedApprovalBoundaryBanner />
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div className="workspace-frame__identity">
            <p className="eyebrow">Accepted approval evidence</p>
            <h2>Accepted Approval Evidence</h2>
            <p>
              Accepted approval evidence is present. This panel shows supplied evidence and binding only; it does not create approval.
            </p>
          </div>
          <div className="approval-package-banner" data-testid="accepted-approvals.statusBanner">
            {showCurrentEvidence ? <span>Evidence loaded.</span> : null}
            {incomplete ? <span>Accepted approval evidence is incomplete.</span> : null}
            {stale ? <span>Accepted approval evidence is stale or expired.</span> : null}
            {expired ? <span>Accepted approval evidence is stale or expired.</span> : null}
            {unsafeFlags ? <span>Accepted approval evidence contains forbidden authority flags.</span> : null}
            <span>Human review required</span>
          </div>
        </div>

        <div className="approval-package-layout">
          <Surface testId="accepted-approvals.summary">
            <div className="section-heading">
              <p className="eyebrow">Evidence summary</p>
              <h3>Evidence, not permission</h3>
            </div>
            <MetadataRow label="Accepted approval id" value={acceptedApprovalSafeText(evidence.acceptedApprovalId)} />
            <MetadataRow label="Accepted approval hash" value={acceptedApprovalSafeText(evidence.acceptedApprovalHash)} />
            <MetadataRow label="Project id" value={acceptedApprovalSafeText(evidence.projectId)} />
            <MetadataRow label="Accepted by" value={acceptedApprovalSafeText(evidence.acceptedBy)} />
            <MetadataRow label="Accepted at" value={DateTimeDisplay.toLocalDisplay(acceptedApprovalSafeText(evidence.acceptedAtUtc))} />
            <MetadataRow
              label="Human review"
              value={<StatusBadge status={evidence.humanReviewRequired ? 'warning' : 'info'}>{evidence.humanReviewRequired ? 'Human review required' : 'Human review status supplied as not required'}</StatusBadge>}
            />
          </Surface>

          <Surface testId="accepted-approvals.binding">
            <div className="section-heading">
              <p className="eyebrow">Subject and workflow binding</p>
              <h3>Binding checks</h3>
            </div>
            <MetadataRow label="Subject kind" value={acceptedApprovalSafeText(evidence.subjectKind)} />
            <MetadataRow label="Subject id" value={acceptedApprovalSafeText(evidence.subjectId)} />
            <MetadataRow label="Subject hash" value={acceptedApprovalSafeText(evidence.subjectHash)} />
            <MetadataRow label="Workflow run id" value={acceptedApprovalSafeText(evidence.workflowRunId)} />
            <MetadataRow label="Workflow step id" value={acceptedApprovalSafeText(evidence.workflowStepId)} />
          </Surface>

          <Surface testId="accepted-approvals.evidenceRefs">
            <div className="section-heading">
              <p className="eyebrow">Evidence references</p>
              <h3>Read-only references</h3>
            </div>
            {evidence.evidenceReferences.length === 0 ? (
              <p>No evidence references supplied.</p>
            ) : (
              <ul className="detail-list">
                {evidence.evidenceReferences.map((reference, index) => (
                  <li key={`${reference}-${index}`}>{acceptedApprovalSafeText(reference)}</li>
                ))}
              </ul>
            )}
          </Surface>

          <Surface testId="accepted-approvals.boundaryMaxims">
            <div className="section-heading">
              <p className="eyebrow">Boundary maxims</p>
              <h3>What this view cannot do</h3>
            </div>
            <ul className="detail-list">
              {[...acceptedApprovalBoundaryMaxims, ...evidence.boundaryMaxims].map((maxim, index) => (
                <li key={`${maxim}-${index}`}>{acceptedApprovalSafeText(maxim)}</li>
              ))}
            </ul>
          </Surface>

          <Surface testId="accepted-approvals.warnings">
            <div className="section-heading">
              <p className="eyebrow">Warnings</p>
              <h3>Safety state</h3>
            </div>
            {showCurrentEvidence ? <p data-testid="accepted-approvals.currentBadge">Accepted approval evidence is available.</p> : null}
            {incomplete ? (
              <p data-testid="accepted-approvals.incompleteWarning">
                Accepted approval evidence is incomplete. It cannot be treated as current approval evidence. Missing: {missingFields.join(', ')}.
              </p>
            ) : null}
            {stale ? <p data-testid="accepted-approvals.staleWarning">Accepted approval evidence is stale or expired. Do not use this as current authority.</p> : null}
            {expired ? <p data-testid="accepted-approvals.expiredWarning">Accepted approval evidence is stale or expired. Do not use this as current authority.</p> : null}
            {evidence.staleReasonCodes?.length ? (
              <ul className="detail-list" data-testid="accepted-approvals.staleReasons">
                {evidence.staleReasonCodes.map((reason) => (
                  <li key={reason}>{acceptedApprovalSafeText(reason)}</li>
                ))}
              </ul>
            ) : null}
            {unsafeFlags ? <p>Forbidden authority flags were supplied. This view refuses to treat them as authority.</p> : null}
          </Surface>
        </div>

        <div className="approval-package-actions" data-testid="accepted-approvals.copyActions">
          <button type="button" className="command-button" onClick={() => copyValue(evidence.acceptedApprovalId, 'Accepted approval id')}>
            Copy Accepted Approval ID
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.acceptedApprovalHash, 'Accepted approval hash')}>
            Copy Accepted Approval Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.subjectHash, 'Subject hash')}>
            Copy Subject Hash
          </button>
          <button type="button" className="command-button" onClick={() => copyValue(evidence.evidenceReferences.join('\n'), 'Evidence references')}>
            Copy Evidence References
          </button>
        </div>
        {copyMessage ? <p className="state-muted" data-testid="accepted-approvals.copyStatus">{copyMessage}</p> : null}
      </section>
    </main>
  );
}

function AcceptedApprovalBoundaryBanner() {
  return (
    <section className="workspace-frame" data-testid="accepted-approvals.boundaryBanner">
      <div className="approval-package-banner">
        <span>{acceptedApprovalBoundaryBanner}</span>
      </div>
    </section>
  );
}
