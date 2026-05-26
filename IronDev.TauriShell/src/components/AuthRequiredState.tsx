import type { ApiStatus, ProductAccessStatus, ProjectSummary, TenantSummary } from '../api/types';
import { ApiStatusBadge } from './ApiStatusBadge';
import { CommandButton } from './CommandButton';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';

interface AuthRequiredStateProps {
  apiStatus: ApiStatus;
  accessStatus: ProductAccessStatus;
  authLabel: string;
  tokenDraft: string;
  email: string;
  password: string;
  isConfigOpen: boolean;
  tenants: TenantSummary[];
  projects: ProjectSummary[];
  selectedTenantId: number | null;
  selectedProjectId: number | null;
  isBusy: boolean;
  errorMessage: string | null;
  onConfigureToken: () => void;
  onRetry: () => void;
  onTokenDraftChange: (value: string) => void;
  onEmailChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onSaveToken: () => void;
  onSignIn: () => void;
  onSelectTenant: (tenantId: number) => void;
  onSelectProject: (projectId: number) => void;
}

export function AuthRequiredState({
  apiStatus,
  accessStatus,
  authLabel,
  tokenDraft,
  email,
  password,
  isConfigOpen,
  tenants,
  projects,
  selectedTenantId,
  selectedProjectId,
  isBusy,
  errorMessage,
  onConfigureToken,
  onRetry,
  onTokenDraftChange,
  onEmailChange,
  onPasswordChange,
  onSaveToken,
  onSignIn,
  onSelectTenant,
  onSelectProject
}: AuthRequiredStateProps) {
  const isApiOffline = accessStatus === 'apiOffline';
  const isApiError = accessStatus === 'apiError';
  const isTenantRequired = accessStatus === 'tenantRequired';
  const isProjectRequired = accessStatus === 'projectRequired';
  const isAuthRequired = accessStatus === 'authRequired' || accessStatus === 'authInvalid';
  const title = getTitle(accessStatus);

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
        ) : isTenantRequired ? (
          <p>Select the tenant context before IronDev loads projects and tickets.</p>
        ) : isProjectRequired ? (
          <p>Select the project context before IronDev loads the ticket queue.</p>
        ) : (
          <p>IronDev.Api is reachable, but ticket data requires a signed-in API session.</p>
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
        {isTenantRequired ? (
          <MetadataRow label="Tenant" value={<StatusBadge status="warning">Required</StatusBadge>} />
        ) : null}
        {isProjectRequired ? (
          <MetadataRow
            label="Project"
            value={
              <StatusBadge status="warning" data-testid="project.status.missing">
                Required
              </StatusBadge>
            }
          />
        ) : null}
      </div>

      {isAuthRequired ? (
        <form
          className="auth-form"
          data-testid="auth.form"
          onSubmit={(event) => {
            event.preventDefault();
            onSignIn();
          }}
        >
          <label>
            <span>Email</span>
            <input
              data-testid="auth.email"
              type="email"
              value={email}
              autoComplete="username"
              onChange={(event) => onEmailChange(event.target.value)}
            />
          </label>
          <label>
            <span>Password</span>
            <input
              data-testid="auth.password"
              type="password"
              value={password}
              autoComplete="current-password"
              onChange={(event) => onPasswordChange(event.target.value)}
            />
          </label>
          <div className="auth-required-state__actions">
            <span data-testid="auth.signIn">
              <CommandButton variant="primary" testId="auth.submit" type="submit" disabled={isBusy}>
                {isBusy ? 'Signing in...' : 'Sign in'}
              </CommandButton>
            </span>
            <CommandButton variant="secondary" testId="app.authState.retry" type="button" onClick={onRetry}>
              Retry connection
            </CommandButton>
            <CommandButton variant="subtle" testId="app.authState.configureToken" type="button" onClick={onConfigureToken}>
              Configure token
            </CommandButton>
          </div>
        </form>
      ) : null}

      {isTenantRequired ? (
        <label className="context-select">
          <span>Tenant</span>
          <select
            data-testid="tenant.selector"
            value={selectedTenantId ?? ''}
            onChange={(event) => onSelectTenant(Number.parseInt(event.target.value, 10))}
            disabled={isBusy}
          >
            <option value="">Select tenant</option>
            {tenants.map((tenant) => (
              <option key={tenant.id} value={tenant.id} data-testid="tenant.option">
                {tenant.name}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      {isProjectRequired ? (
        <label className="context-select">
          <span>Project</span>
          <select
            data-testid="project.selector"
            value={selectedProjectId ?? ''}
            onChange={(event) => onSelectProject(Number.parseInt(event.target.value, 10))}
            disabled={isBusy || projects.length === 0}
          >
            <option value="">Select project</option>
            {projects.map((project) => (
              <option key={project.id} value={project.id} data-testid="project.option">
                {project.name ?? `Project ${project.id}`}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      {!isAuthRequired ? (
        <div className="auth-required-state__actions">
          <CommandButton variant="secondary" testId="app.authState.retry" onClick={onRetry} disabled={isBusy}>
            Retry connection
          </CommandButton>
        </div>
      ) : null}

      {isAuthRequired && isConfigOpen ? (
        <div className="token-config">
          <input
            aria-label="IronDev API token"
            data-testid="auth.tokenInput"
            type="password"
            value={tokenDraft}
            placeholder="Paste local API token"
            onChange={(event) => onTokenDraftChange(event.target.value)}
          />
          <CommandButton
            variant="primary"
            testId="auth.saveToken"
            onClick={onSaveToken}
            disabled={tokenDraft.trim().length === 0}
          >
            Save token
          </CommandButton>
        </div>
      ) : null}

      {errorMessage ? <p className="state-error">{errorMessage}</p> : null}
    </div>
  );
}

function getTitle(status: ProductAccessStatus) {
  switch (status) {
    case 'apiOffline':
      return 'IronDev.Api is offline';
    case 'apiError':
      return 'IronDev.Api needs attention';
    case 'authInvalid':
      return 'Authentication failed';
    case 'tenantRequired':
      return 'Tenant required';
    case 'projectRequired':
      return 'Project required';
    default:
      return 'Authentication required';
  }
}
