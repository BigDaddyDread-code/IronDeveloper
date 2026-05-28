import { MetadataRow } from '../../components/MetadataRow';
import { StatusBadge } from '../../components/StatusBadge';
import type { CodeStandardsEvidence } from '../../api/types';

export function CodeStandardsSummary({ evidence }: { evidence: CodeStandardsEvidence }) {
  const passed = evidence.status.toLowerCase() === 'passed';

  return (
    <section className="chat-build-code-standards" data-testid="chat-build.codeStandardsSummary">
      <div className="section-heading">
        <p className="eyebrow">Code standards</p>
        <h2>Read-only check</h2>
      </div>
      <div className="chat-build-evidence-card chat-build-evidence-card--quiet">
        <MetadataRow label="State" value={<StatusBadge status={passed ? 'ready' : 'warning'}>{evidence.status}</StatusBadge>} />
        <MetadataRow label="Summary" value={evidence.summary} />
        <MetadataRow label="Evidence" value={evidence.evidencePath ?? 'unavailable'} />
      </div>
    </section>
  );
}
