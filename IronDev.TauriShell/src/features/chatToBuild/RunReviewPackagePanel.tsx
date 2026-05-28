import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import { Surface } from '../../design-system/Surface';
import type { OutputVerificationEvidence, RunReviewPackage } from '../../api/types';
import { CodeStandardsSummary } from './CodeStandardsSummary';
import { CommandEvidenceList } from './CommandEvidenceList';
import { GeneratedFilesViewer } from './GeneratedFilesViewer';
import { HumanReviewChecklist } from './HumanReviewChecklist';
import { OutputVerificationPanel } from './OutputVerificationPanel';
import { RiskList } from './RiskList';
import { RunEventTimeline } from './RunEventTimeline';

export function RunReviewPackagePanel({ reviewPackage }: { reviewPackage: RunReviewPackage | null }) {
  const verifications = getVerifications(reviewPackage);

  return (
    <Surface className="chat-build-panel chat-build-review-package" testId="chat-build.reviewPackage">
      <div className="section-heading">
        <p className="eyebrow">Review package</p>
        <h2>Human approval packet</h2>
      </div>
      {reviewPackage ? (
        <>
          <div className="metadata-stack">
            <MetadataRow label="Run" value={reviewPackage.runId} />
            <MetadataRow label="State" value={<StatusBadge status={reviewPackage.state === 'PausedForApproval' ? 'ready' : 'warning'}>{reviewPackage.state}</StatusBadge>} />
            <MetadataRow label="File set hash" value={reviewPackage.fileSetHash} />
          </div>
          <p className="chat-build-safety-note">Paused for human approval. Generated files are sandbox evidence, not repository writes.</p>
          <GeneratedFilesViewer files={reviewPackage.generatedFiles} />
          <CommandEvidenceList commands={reviewPackage.commandEvidence} />
          <OutputVerificationPanel verifications={verifications} />
          <CodeStandardsSummary evidence={reviewPackage.codeStandards} />
          <RiskList risks={reviewPackage.risks} />
          <HumanReviewChecklist items={reviewPackage.humanReviewChecklist} />
          <RunEventTimeline events={reviewPackage.events} />
        </>
      ) : (
        <p className="state-muted">Start a sandbox code run and load the project-scoped review package.</p>
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
