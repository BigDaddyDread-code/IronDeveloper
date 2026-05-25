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
  return (
    <div className="auth-required-state" data-testid="app.authState">
      <div className="auth-required-state__header">
        <p className="eyebrow">Product access</p>
        <h2>Authentication required</h2>
        <p>IronDev.Api is the product boundary. Ticket data loads only after this shell has an API token.</p>
      </div>

      <div className="auth-required-state__metadata">
        <MetadataRow label="API" value={<code>{apiStatus.baseUrl}</code>} />
        <MetadataRow label="Status" value={<ApiStatusBadge status={apiStatus.status} withTestId={false} />} />
        <MetadataRow
          label="Auth"
          value={
            <StatusBadge status="authRequired" data-testid="api.status.authRequired">
              {authLabel}
            </StatusBadge>
          }
        />
      </div>

      <div className="auth-required-state__actions">
        <CommandButton variant="primary" testId="app.authState.configureToken" onClick={onConfigureToken}>
          Configure token
        </CommandButton>
        <CommandButton variant="secondary" testId="app.authState.retry" onClick={onRetry}>
          Retry connection
        </CommandButton>
      </div>

      {isConfigOpen ? (
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
