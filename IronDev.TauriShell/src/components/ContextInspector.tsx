import type { ProjectTicket } from '../api/types';
import { EvidenceCard } from './EvidenceCard';
import { InspectorSection } from './InspectorSection';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';

interface ContextInspectorProps {
  ticket: ProjectTicket | null;
  apiBaseUrl: string;
  projectId: number;
  tokenConfigured: boolean;
}

export function ContextInspector({ ticket, apiBaseUrl, projectId, tokenConfigured }: ContextInspectorProps) {
  return (
    <SurfacePanel className="context-inspector" testId="ticket.inspector">
      <div className="section-heading">
        <p className="eyebrow">Context inspector</p>
        <h2>Evidence</h2>
      </div>

      <InspectorSection title="Boundary">
        <MetadataRow label="API" value={<code>{apiBaseUrl}</code>} />
        <MetadataRow label="Project" value={projectId} />
        <MetadataRow
          label="Auth"
          value={
            <StatusBadge status={tokenConfigured ? 'connected' : 'authRequired'}>
              {tokenConfigured ? 'Token configured' : 'Token needed'}
            </StatusBadge>
          }
        />
      </InspectorSection>

      <InspectorSection title="Selected ticket">
        <MetadataRow label="Ticket" value={ticket ? `#${ticket.id}` : 'none'} />
        <MetadataRow label="Status" value={ticket?.status ?? 'waiting'} />
        <MetadataRow label="Priority" value={ticket?.priority ?? 'unknown'} />
      </InspectorSection>

      <EvidenceCard title="Trace links">
        <p>API-backed trace and evidence records stay out of the shell until IronDev.Api returns them.</p>
        <code>IronDev.Api -&gt; services -&gt; database</code>
      </EvidenceCard>
    </SurfacePanel>
  );
}
