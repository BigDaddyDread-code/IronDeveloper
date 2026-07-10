import type { ApiStatus, ProductAccessStatus, ProjectSummary, TenantSummary } from '../api/types';
import { CommandButton } from './CommandButton';

interface AuthRequiredStateProps {
  apiStatus: ApiStatus;
  accessStatus: ProductAccessStatus;
  authLabel: string;
  tokenDraft: string;
  email: string;
  password: string;
  isConfigOpen: boolean;
  isLocalTestEnvironment?: boolean;
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

// Legacy/debug fallback for the old tickets workspace. The primary entry route
// is owned by FlowShell, SignInRoute, TenantChooser, and ProjectChooser.
export function AuthRequiredState({
  accessStatus,
  email,
  password,
  isLocalTestEnvironment = false,
  isBusy,
  errorMessage,
  onRetry,
  onEmailChange,
  onPasswordChange,
  onSignIn
}: AuthRequiredStateProps) {
  const isAuthRequired = accessStatus === 'authRequired' || accessStatus === 'authInvalid';

  return (
    <div className="auth-required-state" data-testid="app.authState">
      <div className="auth-required-state__header">
        <p className="eyebrow">{isAuthRequired ? 'Sign in' : 'Product access'}</p>
        <h2>{getTitle(accessStatus)}</h2>
        <p>{getMessage(accessStatus)}</p>
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
          <p className="auth-form__flow" data-testid="auth.flowHint">
            Sign in, then continue through the flow entry screens.
          </p>
          {isLocalTestEnvironment ? (
            <p className="auth-form__localtest" data-testid="auth.localtestCredentials">
              LocalTest credentials are prefilled for this environment.
            </p>
          ) : null}
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
          </div>
        </form>
      ) : (
        <div className="auth-required-state__actions">
          <CommandButton variant="secondary" testId="app.authState.retry" onClick={onRetry} disabled={isBusy}>
            Retry connection
          </CommandButton>
        </div>
      )}

      {errorMessage ? (
        <p className="state-error" role="alert">
          {errorMessage}
        </p>
      ) : null}
    </div>
  );
}

function getMessage(status: ProductAccessStatus) {
  switch (status) {
    case 'apiOffline':
      return 'Start the backend, then retry the connection.';
    case 'apiError':
      return 'IronDev.Api responded, but the health check is not passing.';
    case 'authInvalid':
      return 'Your session has expired. Sign in again.';
    case 'tenantRequired':
      return 'Return to the flow entry screen to choose a tenant.';
    case 'projectRequired':
      return 'Return to the flow entry screen to choose a project.';
    default:
      return 'Sign in to continue.';
  }
}

function getTitle(status: ProductAccessStatus) {
  switch (status) {
    case 'apiOffline':
      return 'IronDev.Api is offline';
    case 'apiError':
      return 'IronDev.Api needs attention';
    case 'authInvalid':
      return 'Session expired';
    case 'tenantRequired':
      return 'Tenant required';
    case 'projectRequired':
      return 'Project required';
    default:
      return 'Sign in to IronDev';
  }
}
