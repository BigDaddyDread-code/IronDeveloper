import type { ApiStatus } from '../api/types';
import { ApiStatusBadge } from './ApiStatusBadge';
import { CommandButton } from './CommandButton';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';

interface AuthRequiredStateProps {
  apiStatus: ApiStatus;
  authLabel: string;
  tokenDraft: string;
  isConfigOpen: boolean;
  onConfigureToken: () => void;
  onRetry: () => void;
  onTokenDraftChange: (value: string) => void;
  onSaveToken: () => void;
}

export function AuthRequiredState({
  apiStatus,
  authLabel,
  tokenDraft,
  isConfigOpen,
  onConfigureToken,
  onRetry,
  onTokenDraftChange,
  onSaveToken
}: AuthRequiredStateProps) {
  const isApiOffline = apiStatus.status === 'disconnected';
  const isApiError = apiStatus.status === 'error';
  const isAuthRequired = !isApiOffline && !isApiError;
  const title = isApiOffline
    ? 'IronDev.Api is offline'
    : isApiError
      ? 'IronDev.Api needs attention'
      : 'Authentication required';

  return (
    <div className="auth-required-state" data-testid="app.authState">
      <div className="auth-required-state__header">
        <p className="eyebrow">Product access</p>
        <h2>{title}</h2>
        {isApiOffline ? (
          <>
            <p>Start the backend with:</p>
            <code>dotnet run --project IronDev.Api</code>
          </>
        ) : isApiError ? (
          <p>IronDev.Api responded, but the health check is not passing. Check the local backend and retry.</p>
        ) : (
          <p>IronDev.Api is reachable, but ticket data requires an API token.</p>
        )}
      </div>

      <div className="auth-required-state__metadata">
        <MetadataRow label="API" value={<code>{apiStatus.baseUrl}</code>} />
        <MetadataRow label="Status" value={<ApiStatusBadge status={apiStatus.status} withTestId={false} />} />
        {isAuthRequired ? (
          <MetadataRow
            label="Auth"
            value={
              <StatusBadge status="authRequired" data-testid="api.status.authRequired">
                {authLabel}
              </StatusBadge>
            }
          />
        ) : null}
      </div>

      <div className="auth-required-state__actions">
        <CommandButton variant="secondary" testId="app.authState.retry" onClick={onRetry}>
          Retry connection
        </CommandButton>
        {isAuthRequired ? (
          <CommandButton variant="primary" testId="app.authState.configureToken" onClick={onConfigureToken}>
            Configure token
          </CommandButton>
        ) : null}
      </div>

      {isAuthRequired && isConfigOpen ? (
        <div className="token-config">
          <input
            aria-label="IronDev API token"
            type="password"
            value={tokenDraft}
            placeholder="Paste local API token"
            onChange={(event) => onTokenDraftChange(event.target.value)}
          />
          <CommandButton variant="primary" onClick={onSaveToken} disabled={tokenDraft.trim().length === 0}>
            Save token
          </CommandButton>
        </div>
      ) : null}
    </div>
  );
}
