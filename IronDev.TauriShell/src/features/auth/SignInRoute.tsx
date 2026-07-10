import { useCallback, useEffect, useRef, useState } from 'react';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface SignInRouteProps {
  onOpenSettings: () => void;
}

export function SignInRoute({ onOpenSettings }: SignInRouteProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const didSetInitialFocus = useRef(false);
  const [lastRetry, setLastRetry] = useState<string | null>(null);
  const isLocalTestEnvironment = Boolean(session.environmentInfo?.isTestEnvironment);
  const inlineError = session.errorMessage;
  const inlineMessage = !inlineError ? session.sessionMessage : null;

  useEffect(() => {
    if (didSetInitialFocus.current || session.apiStatus.status === 'loading') {
      return;
    }
    const target = session.email.trim().length > 0 ? passwordRef.current : emailRef.current;
    target?.focus();
    didSetInitialFocus.current = true;
  }, [session.apiStatus.status, session.email]);

  const signIn = useCallback(async () => {
    await session.signIn({ email: session.email.trim(), password: session.password });
    await project.refreshProjectContext();
  }, [project, session]);

  const retryConnection = useCallback(async () => {
    setLastRetry(new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }));
    await project.refreshProjectContext();
  }, [project]);

  return (
    <main className="fl-root fl-auth-root" data-testid="auth.route" aria-label="Sign in">
      <header className="fl-auth-header">
        <div className="fl-brand">
          <span className="fl-brand-mark">I</span>
          <span>IronDev</span>
        </div>
        <span className="fl-auth-environment" data-testid="auth.apiStatusChip">
          {session.environmentInfo?.environment ? `${session.environmentInfo.environment} · ` : ''}
          {apiStatusLabel(session.apiStatus.status)}
        </span>
      </header>

      <section className="fl-auth-frame" aria-labelledby="auth-title">
        <div className="fl-auth-intro">
          <p className="fl-plabel">Sign in</p>
          <h1 id="auth-title" className="fl-h1">
            Continue to your projects and governed work.
          </h1>
          {isLocalTestEnvironment ? (
            <p className="fl-auth-localtest" data-testid="auth.localtestCredentials">
              LocalTest credentials
            </p>
          ) : null}
        </div>

        <form
          className="fl-auth-card fl-auth-form"
          data-testid="auth.form"
          onSubmit={(event) => {
            event.preventDefault();
            void signIn();
          }}
        >
          <p className="fl-auth-flow" data-testid="auth.flowHint">
            Sign in, then select a project to continue.
          </p>
          <label className="fl-auth-field">
            <span>Email</span>
            <input
              ref={emailRef}
              data-testid="auth.email"
              type="email"
              value={session.email}
              autoComplete="username"
              required
              aria-invalid={Boolean(inlineError)}
              aria-describedby={inlineError ? 'auth-error' : undefined}
              onChange={(event) => {
                session.clearError();
                session.setEmail(event.target.value);
              }}
            />
          </label>
          <label className="fl-auth-field">
            <span>Password</span>
            <input
              ref={passwordRef}
              data-testid="auth.password"
              type="password"
              value={session.password}
              autoComplete="current-password"
              required
              aria-invalid={Boolean(inlineError)}
              aria-describedby={inlineError ? 'auth-error' : undefined}
              onChange={(event) => {
                session.clearError();
                session.setPassword(event.target.value);
              }}
            />
          </label>
          <button className="fl-btn fl-pri" data-testid="auth.submit" type="submit" disabled={session.isAuthBusy}>
            {session.isAuthBusy ? 'Signing in...' : 'Sign in'}
          </button>
          {inlineError ? (
            <p id="auth-error" className="fl-auth-error" data-testid="auth.error" role="alert">
              {inlineError}
            </p>
          ) : null}
          {inlineMessage ? (
            <p className="fl-auth-note" data-testid="auth.sessionMessage" role="status">
              {inlineMessage}
            </p>
          ) : null}
        </form>

        <details className="fl-auth-connection-details" data-testid="auth.connectionDetails">
          <summary>Connection details</summary>
          <dl>
            <div>
              <dt>API URL</dt>
              <dd>
                <code>{session.apiStatus.baseUrl}</code>
              </dd>
            </div>
            <div>
              <dt>Environment</dt>
              <dd>{session.environmentInfo?.environment ?? 'Not reported yet'}</dd>
            </div>
            <div>
              <dt>API status</dt>
              <dd>{apiStatusLabel(session.apiStatus.status)}</dd>
            </div>
            <div>
              <dt>Last retry</dt>
              <dd>{lastRetry ?? 'Not retried'}</dd>
            </div>
          </dl>
          <div className="fl-auth-detail-actions">
            <button
              className="fl-btn fl-mini"
              data-testid="app.authState.retry"
              type="button"
              disabled={project.isRefreshing}
              onClick={() => void retryConnection()}
            >
              Retry connection
            </button>
            <button
              className="fl-btn fl-mini"
              data-testid="auth.connection.settings"
              type="button"
              onClick={onOpenSettings}
            >
              Connection settings
            </button>
          </div>
          <details className="fl-auth-advanced" data-testid="auth.advanced">
            <summary>Advanced</summary>
            <label className="fl-auth-field">
              <span>API token</span>
              <input
                aria-label="IronDev API token"
                data-testid="auth.tokenInput"
                type="password"
                value={session.tokenDraft}
                autoComplete="off"
                onChange={(event) => session.setTokenDraft(event.target.value)}
              />
            </label>
            <button
              className="fl-btn fl-mini"
              data-testid="auth.saveToken"
              type="button"
              disabled={session.tokenDraft.trim().length === 0}
              onClick={() => {
                session.saveToken();
                if (session.tokenDraft.trim().length > 0) {
                  void project.refreshProjectContext();
                }
              }}
            >
              Save API token
            </button>
          </details>
        </details>
      </section>
    </main>
  );
}

function apiStatusLabel(status: string) {
  switch (status) {
    case 'connected':
      return 'API connected';
    case 'loading':
      return 'Checking API';
    case 'disconnected':
      return 'API offline';
    case 'error':
      return 'API needs attention';
    default:
      return `API ${status}`;
  }
}
