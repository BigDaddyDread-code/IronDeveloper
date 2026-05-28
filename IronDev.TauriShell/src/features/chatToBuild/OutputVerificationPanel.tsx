import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import type { OutputVerificationEvidence } from '../../api/types';

export function OutputVerificationPanel({ verifications }: { verifications: OutputVerificationEvidence[] }) {
  return (
    <section className="chat-build-output" data-testid="chat-build.outputVerification">
      <div className="section-heading">
        <p className="eyebrow">Verification</p>
        <h2>Output checks</h2>
      </div>
      {verifications.length > 0 ? (
        <div className="chat-build-list">
          {verifications.map((verification, index) => (
            <article key={`${verification.expected}-${index}`} className="chat-build-evidence-card">
              <MetadataRow
                label="State"
                value={<StatusBadge status={verification.verified ? 'ready' : 'danger'}>{verification.verified ? 'Verified' : 'Failed'}</StatusBadge>}
              />
              <MetadataRow label="Expected" value={verification.expected} />
              <MetadataRow label="Actual" value={verification.actual || 'unavailable'} />
              <MetadataRow label="Evidence" value={verification.evidencePath ?? 'unavailable'} />
            </article>
          ))}
        </div>
      ) : (
        <p className="state-muted">No output verification evidence has been loaded.</p>
      )}
    </section>
  );
}
