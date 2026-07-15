import { useCallback, useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { AiConnectionMetadata } from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';
import { navigateProductPath, settingsPath } from '../navigation/productRoutes';

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
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <button
            className="fl-btn fl-mini"
            type="button"
            onClick={() => typeof session.config.selectedProjectId === 'number' && navigateProductPath(settingsPath(session.config.selectedProjectId, 'agents'))}
            data-testid="flow.settings.aiConnections.openAgents"
          >Open project agent profiles</button>
          <StatusBadge status={connections.length === 0 ? 'warning' : 'ready'} data-testid="flow.settings.aiConnections.count">
            {connections.length === 0 ? 'None configured' : `${connections.length} available`}
          </StatusBadge>
        </div>
      </div>

      {connections.length === 0 ? (
        <p className="fl-empty" data-testid="flow.settings.aiConnections.empty">
          No AI connection metadata was returned for this tenant.
        </p>
      ) : (
        <div style={{ display: 'grid', gap: 10, marginTop: 12 }}>
          {connections.map((connection, index) => (
            <ConnectionRow
              key={connection.id}
              connection={connection}
              index={index}
              onConfigure={async (credential, reason) => {
                const outcome = await session.client.configureAiConnectionCredential(connection.id, { credential, reason });
                const updated = outcome.connection;
                if (updated) {
                  setConnections((current) => current.map((item) => item.id === updated.id ? updated : item));
                }
                return outcome.succeeded ? null : outcome.failureReason ?? 'Credential was not stored.';
              }}
              onRevoke={async (reason) => {
                const outcome = await session.client.revokeAiConnectionCredential(connection.id, { reason });
                const updated = outcome.connection;
                if (updated) {
                  setConnections((current) => current.map((item) => item.id === updated.id ? updated : item));
                }
                return outcome.succeeded ? null : outcome.failureReason ?? 'Credential was not revoked.';
              }}
              onTest={async () => {
                const outcome = await session.client.testAiConnection(connection.id);
                const updated = outcome.connection;
                if (updated) {
                  setConnections((current) => current.map((item) => item.id === updated.id ? updated : item));
                }
                return outcome.succeeded ? null : outcome.failureReason ?? 'Connection test failed.';
              }}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function ConnectionRow({
  connection,
  index,
  onConfigure,
  onRevoke,
  onTest
}: {
  connection: AiConnectionMetadata;
  index: number;
  onConfigure: (credential: string, reason?: string | null) => Promise<string | null>;
  onRevoke: (reason?: string | null) => Promise<string | null>;
  onTest: () => Promise<string | null>;
}) {
  const availability = connection.enabled && connection.tenantAvailable && connection.projectAvailable;
  const models = connection.availableModels.length === 0 ? 'No models returned' : connection.availableModels.join(', ');
  const [credential, setCredential] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState<'configure' | 'revoke' | 'test' | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const configure = async () => {
    if (!credential.trim() || busy) {
      setError('Enter a credential before saving.');
      return;
    }

    setBusy('configure');
    setError(null);
    setMessage(null);
    try {
      const failure = await onConfigure(credential.trim(), reason.trim() || null);
      if (failure) {
        setError(failure);
        return;
      }

      setCredential('');
      setMessage('Credential stored. It will not be shown again.');
    } catch (e) {
      setError(readMutationError(e, 'Credential was not stored.'));
    } finally {
      setBusy(null);
    }
  };

  const revoke = async () => {
    if (busy) {
      return;
    }

    setBusy('revoke');
    setError(null);
    setMessage(null);
    try {
      const failure = await onRevoke(reason.trim() || null);
      if (failure) {
        setError(failure);
        return;
      }

      setCredential('');
      setMessage('Credential revoked.');
    } catch (e) {
      setError(readMutationError(e, 'Credential was not revoked.'));
    } finally {
      setBusy(null);
    }
  };

  const testConnection = async () => {
    if (busy) return;
    setBusy('test');
    setError(null);
    setMessage(null);
    try {
      const failure = await onTest();
      if (failure) {
        setError(failure);
        return;
      }
      setMessage('Connection test passed.');
    } catch (e) {
      setError(readMutationError(e, 'Connection test failed.'));
    } finally {
      setBusy(null);
    }
  };

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
        <dt>Last rotated</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.rotated`}>{formatUtc(connection.credentialRotatedUtc)}</dd>
        <dt>Last revoked</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.revoked`}>{formatUtc(connection.credentialRevokedUtc)}</dd>
        <dt>Models</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.models`}>{models}</dd>
        <dt>Last successful test</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.lastSuccess`}>{formatUtc(connection.lastSuccessfulTestUtc)}</dd>
        <dt>Last failed test</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.aiConnections.connection.${index}.lastFailure`}>{formatUtc(connection.lastFailedTestUtc)}</dd>
      </dl>

      <div style={{ display: 'grid', gap: 8, marginTop: 10 }} data-testid={`flow.settings.aiConnections.connection.${index}.credentialActions`}>
        <label className="fl-plabel" style={{ marginTop: 0 }} htmlFor={`ai-credential-${index}`}>
          Credential
        </label>
        <input
          id={`ai-credential-${index}`}
          type="password"
          value={credential}
          onChange={(event) => setCredential(event.target.value)}
          placeholder={connection.credentialConfigured ? 'Replace credential' : 'Enter credential'}
          data-testid={`flow.settings.aiConnections.connection.${index}.credentialInput`}
        />
        <label className="fl-plabel" style={{ marginTop: 0 }} htmlFor={`ai-credential-reason-${index}`}>
          Reason
        </label>
        <input
          id={`ai-credential-reason-${index}`}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Optional lifecycle note"
          data-testid={`flow.settings.aiConnections.connection.${index}.reason`}
        />
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button
            className="fl-btn fl-pri"
            type="button"
            disabled={busy !== null || credential.trim().length === 0}
            onClick={() => void configure()}
            data-testid={`flow.settings.aiConnections.connection.${index}.saveCredential`}
          >
            {busy === 'configure' ? 'Saving...' : connection.credentialConfigured ? 'Replace credential' : 'Save credential'}
          </button>
          <button
            className="fl-btn"
            type="button"
            disabled={busy !== null || !availability}
            onClick={() => void testConnection()}
            data-testid={`flow.settings.aiConnections.connection.${index}.test`}
          >
            {busy === 'test' ? 'Testing...' : 'Test connection'}
          </button>
          <button
            className="fl-btn"
            type="button"
            disabled={busy !== null}
            onClick={() => void revoke()}
            data-testid={`flow.settings.aiConnections.connection.${index}.revokeCredential`}
          >
            {busy === 'revoke' ? 'Revoking...' : 'Revoke credential'}
          </button>
        </div>
        {message ? <p className="fl-empty" data-testid={`flow.settings.aiConnections.connection.${index}.message`}>{message}</p> : null}
        {error ? <div className="fl-error" data-testid={`flow.settings.aiConnections.connection.${index}.error`}>{error}</div> : null}
      </div>

      <p className="fl-empty" style={{ marginTop: 8 }} data-testid={`flow.settings.aiConnections.connection.${index}.boundary`}>
        {connection.boundary}
      </p>
    </article>
  );
}

function formatUtc(value?: string | null) {
  if (!value) {
    return 'Never';
  }

  return new Date(value).toLocaleString();
}

function readMutationError(error: unknown, fallback: string) {
  if (error instanceof IronDevApiError) {
    const body = error.body as { failureReason?: string } | undefined;
    return body?.failureReason ?? error.message;
  }

  return error instanceof Error ? error.message : fallback;
}
