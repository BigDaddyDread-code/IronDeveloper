import { useCallback, useEffect, useState } from 'react';
import type { AiConnectionMetadata } from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';

type LoadState = 'loading' | 'ready' | 'error';

export function AiConnectionsPanel() {
  const session = useSessionContext();
  const [connections, setConnections] = useState<AiConnectionMetadata[]>([]);
  const [state, setState] = useState<LoadState>('loading');
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setState('loading');
    setError('');
    try {
      const result = await session.client.listAiConnections();
      setConnections(result);
      setState('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'AI connections could not be loaded.');
      setState('error');
    }
  }, [session.client]);

  useEffect(() => {
    void load();
  }, [load]);

  if (state === 'loading') {
    return <p className="fl-empty" data-testid="flow.settings.aiConnections.loading">Loading AI connections...</p>;
  }

  if (state === 'error') {
    return (
      <div className="fl-error" data-testid="flow.settings.aiConnections.error">
        AI connections did not load: {error}
      </div>
    );
  }

  return (
    <section className="fl-panel-box" data-testid="flow.settings.aiConnections" aria-labelledby="ai-connections-heading">
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'flex-start' }}>
        <div>
          <p className="fl-plabel" style={{ marginTop: 0 }}>AI connections</p>
          <h2 id="ai-connections-heading" style={{ margin: '4px 0 0', fontSize: 18 }}>Tenant connection metadata</h2>
        </div>
        <StatusBadge status={connections.length === 0 ? 'warning' : 'ready'} data-testid="flow.settings.aiConnections.count">
          {connections.length === 0 ? 'None configured' : `${connections.length} available`}
        </StatusBadge>
      </div>

      {connections.length === 0 ? (
        <p className="fl-empty" data-testid="flow.settings.aiConnections.empty">
          No AI connection metadata was returned for this tenant.
        </p>
      ) : (
        <div style={{ display: 'grid', gap: 10, marginTop: 12 }}>
          {connections.map((connection, index) => (
            <ConnectionRow key={connection.id} connection={connection} index={index} />
          ))}
        </div>
      )}
    </section>
  );
}

function ConnectionRow({ connection, index }: { connection: AiConnectionMetadata; index: number }) {
  const availability = connection.enabled && connection.tenantAvailable && connection.projectAvailable;
  const models = connection.availableModels.length === 0 ? 'No models returned' : connection.availableModels.join(', ');
  return (
    <article
      data-testid={`flow.settings.aiConnections.connection.${index}`}
      style={{
        borderTop: '1px solid var(--fl-line)',
        paddingTop: 10
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'center' }}>
        <div>
          <strong>{connection.displayName}</strong>
          <p className="fl-empty" style={{ marginTop: 2 }} data-testid={`flow.settings.aiConnections.connection.${index}.version`}>
            {connection.version}
          </p>
        </div>
        <StatusBadge status={availability ? 'ready' : 'warning'} data-testid={`flow.settings.aiConnections.connection.${index}.availability`}>
          {availability ? 'Available' : 'Unavailable'}
        </StatusBadge>
      </div>

      <dl className="fl-kv" style={{ marginTop: 10 }}>
        <dt>Provider</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.provider`}>{connection.providerKind}</dd>
        <dt>Controlled endpoint</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.endpoint`}>{connection.controlledEndpoint}</dd>
        <dt>Credential</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.credential`}>{connection.credentialStatus}</dd>
        <dt>Models</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.models`}>{models}</dd>
      </dl>

      <p className="fl-empty" style={{ marginTop: 8 }} data-testid={`flow.settings.aiConnections.connection.${index}.boundary`}>
        {connection.boundary}
      </p>
    </article>
  );
}
