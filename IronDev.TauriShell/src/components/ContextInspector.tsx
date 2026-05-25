import type { BuildReadinessResult, ProjectTicket, TicketReadinessLoadStatus } from '../api/types';
import { EvidenceCard } from './EvidenceCard';
import { InspectorSection } from './InspectorSection';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';

interface ContextInspectorProps {
  ticket: ProjectTicket | null;
  readiness: BuildReadinessResult | null;
  readinessStatus: TicketReadinessLoadStatus;
  apiBaseUrl: string;
  projectId: number | null;
  projectStatus: 'selected' | 'missing' | 'fallback';
  tokenConfigured: boolean;
}

export function ContextInspector({
  ticket,
  readiness,
  readinessStatus,
  apiBaseUrl,
  projectId,
  projectStatus,
  tokenConfigured
}: ContextInspectorProps) {
  const affectedFiles = splitList(ticket?.linkedFilePaths);
  const affectedSymbols = splitList(ticket?.linkedSymbols);
  const hasSourceDocument = Boolean(ticket?.sourceDocumentVersionId);
  const warnings = [
    ...(readiness?.warnings ?? []),
    ...(readiness?.blockingIssues ?? []),
    ...(!ticket?.contextSummary ? ['Context summary is not exposed for this ticket.'] : []),
    ...(readinessStatus === 'unavailable' ? ['Build readiness endpoint did not return a usable result.'] : [])
  ];
  const projectLabel =
    projectStatus === 'selected'
      ? `Project ${projectId}`
      : projectStatus === 'fallback'
        ? `Fallback project ${projectId}`
        : 'Project required';

  return (
    <SurfacePanel className="context-inspector" testId="ticket.inspector">
      <div className="section-heading">
        <p className="eyebrow">Context inspector</p>
        <h2>Evidence</h2>
      </div>

      <InspectorSection title="Boundary" testId="ticket.inspector.evidence">
        <MetadataRow label="API" value={<code>{apiBaseUrl}</code>} />
        <MetadataRow label="Project" value={projectLabel} />
        <MetadataRow
          label="Auth"
          value={
            <StatusBadge status={tokenConfigured ? 'connected' : 'authRequired'}>
              {tokenConfigured ? 'Token configured' : 'Token needed'}
            </StatusBadge>
          }
        />
        <MetadataRow label="Ticket" value={ticket ? `#${ticket.id}` : 'None selected'} />
      </InspectorSection>

      <InspectorSection title="Linked documents" testId="ticket.inspector.linkedDocuments">
        {hasSourceDocument ? (
          <MetadataRow label="Source version" value={`Document version #${ticket?.sourceDocumentVersionId}`} />
        ) : (
          <p className="state-muted">No source document link is exposed by the current ticket payload.</p>
        )}
      </InspectorSection>

      <InspectorSection title="Related decisions" testId="ticket.inspector.decisions">
        <p className="state-muted">Decision links are not exposed by the current ticket detail endpoint.</p>
      </InspectorSection>

      <InspectorSection title="Affected files" testId="ticket.inspector.affectedFiles">
        {affectedFiles.length > 0 ? <InspectorList items={affectedFiles} /> : <p className="state-muted">No affected files exposed.</p>}
      </InspectorSection>

      <InspectorSection title="Affected symbols" testId="ticket.inspector.affectedSymbols">
        {affectedSymbols.length > 0 ? (
          <InspectorList items={affectedSymbols} />
        ) : (
          <p className="state-muted">No affected symbols exposed.</p>
        )}
      </InspectorSection>

      <InspectorSection title="Build readiness" testId="ticket.inspector.buildReadiness">
        <MetadataRow label="State" value={<StatusBadge status={readiness?.isReady ? 'ready' : 'warning'}>{readinessStateLabel(readinessStatus, readiness)}</StatusBadge>} />
        <p>{readiness?.message ?? 'Readiness has not been refreshed for the selected ticket.'}</p>
      </InspectorSection>

      <InspectorSection title="Risks / warnings" testId="ticket.inspector.warnings">
        {warnings.length > 0 ? <InspectorList items={warnings} /> : <p className="state-muted">No warnings exposed.</p>}
      </InspectorSection>

      <EvidenceCard title="Trace links">
        <div data-testid="ticket.inspector.traceLinks">
          {ticket?.sourceChatSessionId || ticket?.sourceChatMessageId ? (
            <>
              <MetadataRow label="Chat session" value={ticket.sourceChatSessionId ?? 'Unavailable'} />
              <MetadataRow label="Chat message" value={ticket.sourceChatMessageId ?? 'Unavailable'} />
            </>
          ) : (
            <p className="state-muted">Trace links are not exposed for this ticket yet.</p>
          )}
        </div>
      </EvidenceCard>
    </SurfacePanel>
  );
}

function InspectorList({ items }: { items: string[] }) {
  return (
    <ul className="inspector-list">
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  );
}

function splitList(value?: string | null) {
  if (!value) {
    return [];
  }

  return value
    .split(/\r?\n|;|\|/)
    .map((item) => item.replace(/^[-*]\s*/, '').trim())
    .filter(Boolean);
}

function readinessStateLabel(status: TicketReadinessLoadStatus, readiness: BuildReadinessResult | null) {
  if (status === 'loaded') {
    return readiness?.isReady ? 'Ready' : 'Needs attention';
  }

  if (status === 'loading') {
    return 'Checking';
  }

  if (status === 'error') {
    return 'Error';
  }

  if (status === 'unavailable') {
    return 'Unavailable';
  }

  return 'Not checked';
}
