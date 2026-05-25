import type { ProjectTicket } from '../api/types';
import { StatusBadge } from './StatusBadge';

interface ContextInspectorProps {
  ticket: ProjectTicket | null;
  apiBaseUrl: string;
  projectId: number;
  tokenConfigured: boolean;
}

export function ContextInspector({ ticket, apiBaseUrl, projectId, tokenConfigured }: ContextInspectorProps) {
  return (
    <section className="surface-panel context-inspector" data-testid="ticket.inspector">
      <div className="section-heading">
        <p className="eyebrow">CONTEXT INSPECTOR</p>
        <h2>Evidence</h2>
      </div>

      <div className="inspector-section">
        <span>API boundary</span>
        <strong>{apiBaseUrl}</strong>
      </div>
      <div className="inspector-section">
        <span>Project</span>
        <strong>{projectId}</strong>
      </div>
      <div className="inspector-section">
        <span>Auth</span>
        <StatusBadge status={tokenConfigured ? 'connected' : 'unauthenticated'}>
          {tokenConfigured ? 'token configured' : 'token needed'}
        </StatusBadge>
      </div>
      <div className="inspector-section">
        <span>Selected ticket</span>
        <strong>{ticket ? `#${ticket.id}` : 'none'}</strong>
      </div>
      <div className="inspector-section">
        <span>Trace links</span>
        <strong>API-backed placeholder</strong>
      </div>
    </section>
  );
}
