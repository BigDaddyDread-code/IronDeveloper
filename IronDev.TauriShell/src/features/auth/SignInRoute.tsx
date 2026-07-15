import { useCallback, useEffect, useRef, useState } from 'react';
import { IronDevBrand } from '../../components/IronDevBrand';
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
  const [resetCommandCopied, setResetCommandCopied] = useState(false);
  const preflight = session.localTestPreflight;
  const isLocalTestEnvironment = Boolean(
    session.environmentInfo?.isTestEnvironment ||
      preflight?.environment === 'LocalTest' ||
      (preflight?.state === 'ApiIdentityMismatch' && preflight.resetCommand)
  );
  const resetCommand = isLocalTestEnvironment ? preflight?.resetCommand : null;
  const blocksLocalTestSignIn = isLocalTestEnvironment && preflight?.state !== 'LocalTestReady';
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
    if (await session.signIn({ email: session.email.trim(), password: session.password })) {
      await project.refreshProjectContext();
    }
  }, [project, session]);

  const retryConnection = useCallback(async () => {
    setLastRetry(new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }));
    await project.refreshProjectContext();
  }, [project]);

  return (
    <main className="fl-root fl-auth-root" data-testid="auth.route" aria-label="Sign in">
      <header className="fl-auth-header">
        <IronDevBrand descriptor />
        <span className="fl-auth-environment" data-testid="auth.apiStatusChip">
          {preflight?.environment && preflight.environment !== 'Unknown'
            ? `${preflight.environment} · `
            : session.environmentInfo?.environment
              ? `${session.environmentInfo.environment} · `
              : ''}
          {preflightStatusLabel(session.apiStatus.status, preflight?.state, preflight?.environment)}
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
          <button
            className="fl-btn fl-pri"
            data-testid="auth.submit"
            type="submit"
            disabled={session.isAuthBusy || blocksLocalTestSignIn}
          >
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
          {resetCommand ? (
            <section className="fl-auth-remedy" data-testid="auth.localtestRemedy" aria-label="LocalTest recovery">
              <strong>Exact safe action</strong>
              <p>{preflight?.nextSafeAction}</p>
              <div className="fl-auth-command-row">
                <code data-testid="auth.localtestResetCommand">{resetCommand}</code>
                <button
                  className="fl-btn fl-mini"
                  data-testid="auth.copyLocaltestResetCommand"
                  type="button"
                  onClick={() => {
                    void navigator.clipboard.writeText(resetCommand).then(() => setResetCommandCopied(true));
                  }}
                >
                  {resetCommandCopied ? 'Copied' : 'Copy command'}
                </button>
              </div>
            </section>
          ) : null}
        </form>

        <details
          className="fl-auth-connection-details"
          data-testid="auth.connectionDetails"
          open={isLocalTestEnvironment}
        >
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
              <dd data-testid="auth.localtestEnvironment">
                {preflight?.environment ?? session.environmentInfo?.environment ?? 'Not reported yet'}
              </dd>
            </div>
            <div>
              <dt>Database</dt>
              <dd data-testid="auth.localtestDatabase">
                {preflight?.database ?? session.environmentInfo?.database ?? 'Not reported yet'}
              </dd>
            </div>
            <div>
              <dt>Front door</dt>
              <dd data-testid="auth.localtestSeedStatus">
                {preflightStatusLabel(session.apiStatus.status, preflight?.state, preflight?.environment)}
              </dd>
            </div>
            <div>
              <dt>API build</dt>
              <dd data-testid="auth.localtestApiBuild">{preflight?.apiBuildIdentity ?? 'Not reported yet'}</dd>
            </div>
            <div>
              <dt>Repository</dt>
              <dd data-testid="auth.localtestRepositoryCommit">
                {preflight?.launcherRepositoryCommit ?? 'Not reported yet'}
              </dd>
            </div>
            <div>
              <dt>Session</dt>
              <dd data-testid="auth.localtestSessionId">{preflight?.sessionId ?? 'Not reported yet'}</dd>
            </div>
            <div>
              <dt>Next action</dt>
              <dd data-testid="auth.localtestNextAction">
                {preflight?.nextSafeAction ?? session.apiStatus.message}
              </dd>
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

function preflightStatusLabel(apiStatus: string, preflightState?: string, environment?: string) {
  if (apiStatus !== 'connected') {
    return apiStatusLabel(apiStatus);
  }

  switch (preflightState) {
    case 'LocalTestReady':
      return 'LocalTest ready';
    case 'ApiConnected':
      return 'API connected · checks incomplete';
    case 'WrongEnvironment':
      return environment === 'LocalTest' ? 'WrongEnvironment' : 'API connected';
    case undefined:
      return 'API connected';
    default:
      return preflightState;
  }
}
