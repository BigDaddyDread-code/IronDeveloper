import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { Surface } from '../../design-system/Surface';
import type { OutputVerificationEvidence, RunReviewPackage } from '../../api/types';
import { CommandEvidenceList } from './CommandEvidenceList';
import { GeneratedFilesViewer } from './GeneratedFilesViewer';
import { HumanReviewChecklist } from './HumanReviewChecklist';
import { OutputVerificationPanel } from './OutputVerificationPanel';
import { RunEventTimeline } from './RunEventTimeline';

export function RunReviewPackagePanel({ reviewPackage }: { reviewPackage: RunReviewPackage | null }) {
  const verifications = getVerifications(reviewPackage);

  return (
    <Surface className="chat-build-panel chat-build-review-package" testId="chat-build.reviewPackage">
      <div className="section-heading">
        <p className="eyebrow">Review package</p>
        <h2>Run review package</h2>
      </div>
      {reviewPackage ? (
        <>
          <div className="metadata-stack">
            <MetadataRow label="Run" value={reviewPackage.runId} />
            <MetadataRow label="State" value={<StatusBadge status={reviewPackage.state === 'PausedForApproval' ? 'ready' : 'warning'}>{reviewPackage.state}</StatusBadge>} />
            <MetadataRow label="File set hash" value={reviewPackage.fileSetHash} />
            <MetadataRow label="Code standards" value={`${reviewPackage.codeStandards.status}: ${reviewPackage.codeStandards.summary}`} />
          </div>
          {reviewPackage.risks.length > 0 ? (
            <section className="chat-build-risks" data-testid="chat-build.risks">
              <p className="section-subtitle">Risks</p>
              <ul>
                {reviewPackage.risks.map((risk) => (
                  <li key={risk}>{risk}</li>
                ))}
              </ul>
            </section>
          ) : null}
          <GeneratedFilesViewer files={reviewPackage.generatedFiles} />
          <CommandEvidenceList commands={reviewPackage.commandEvidence} />
          <OutputVerificationPanel verifications={verifications} />
          <HumanReviewChecklist items={reviewPackage.humanReviewChecklist} />
          <RunEventTimeline events={reviewPackage.events} />
        </>
      ) : (
        <p className="state-muted">Start a disposable code run and load the project-scoped review package.</p>
      )}
    </Surface>
  );
}

function getVerifications(reviewPackage: RunReviewPackage | null): OutputVerificationEvidence[] {
  if (!reviewPackage) {
    return [];
  }

  return reviewPackage.outputVerifications?.length > 0
    ? reviewPackage.outputVerifications
    : [reviewPackage.outputVerification].filter(Boolean);
}
