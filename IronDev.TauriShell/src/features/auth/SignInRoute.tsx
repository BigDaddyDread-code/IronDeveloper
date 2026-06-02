import { useCallback } from 'react';
import { AuthRequiredState } from '../../components/AuthRequiredState';
import { StatusFooter } from '../../shell/StatusFooter';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

export function SignInRoute() {
  const session = useSessionContext();
  const project = useProjectContext();

  const signIn = useCallback(async () => {
    await session.signIn({ email: session.email.trim(), password: session.password });
    await project.refreshProjectContext();
  }, [project, session]);

  return (
    <main className="auth-route" data-testid="auth.route" aria-label="Sign in">
      <div className="auth-route__brand">
        <div>
          <p className="eyebrow">IronDev</p>
          <h1>Sign in</h1>
          <p>Sign in, then select a project to continue.</p>
        </div>
        <StatusFooter apiStatus={session.apiStatus} environmentInfo={session.environmentInfo} />
      </div>
      <AuthRequiredState
        apiStatus={session.apiStatus}
        accessStatus={project.accessStatus}
        authLabel={session.tokenConfigured ? 'Token rejected' : 'Missing token'}
        tokenDraft={session.tokenDraft}
        email={session.email}
        password={session.password}
        isConfigOpen={session.isTokenEditorOpen}
        isLocalTestEnvironment={Boolean(session.environmentInfo?.isTestEnvironment)}
        tenants={project.tenants}
        projects={project.projects}
        selectedTenantId={project.selectedTenantId}
        selectedProjectId={project.selectedProjectId}
        isBusy={session.isAuthBusy || project.isRefreshing}
        errorMessage={session.errorMessage}
        onConfigureToken={() => session.setTokenEditorOpen(!session.isTokenEditorOpen)}
        onRetry={() => void project.refreshProjectContext()}
        onTokenDraftChange={session.setTokenDraft}
        onEmailChange={session.setEmail}
        onPasswordChange={session.setPassword}
        onSaveToken={() => {
          session.saveToken();
          if (session.tokenDraft.length === 0) {
            project.setProjectAccessStatus('authRequired');
          }
        }}
        onSignIn={() => void signIn()}
        onSelectTenant={project.selectTenantContext}
        onSelectProject={project.selectProjectContext}
      />
    </main>
  );
}
