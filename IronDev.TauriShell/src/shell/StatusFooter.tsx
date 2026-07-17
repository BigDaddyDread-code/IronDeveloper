import type { ApiStatus, EnvironmentInfo } from '../api/types';
import { ApiStatusBadge } from '../components/ApiStatusBadge';
import { uiBuildInfo } from '../app/buildInfo';

interface StatusFooterProps {
  apiStatus: ApiStatus;
  environmentInfo: EnvironmentInfo | null;
}

export function StatusFooter({ apiStatus, environmentInfo }: StatusFooterProps) {
  const environment = environmentInfo?.environment ?? 'Environment unknown';
  const apiHost = getApiHost(apiStatus.baseUrl);

  return (
    <div className="shell-footer shell-footer--header" data-testid="app.versionStrip" aria-label="UI build and environment identity">
      <span className="api-status" data-testid="app.apiStatus">
        <ApiStatusBadge status={apiStatus.status} />
      </span>
      <span data-testid="app.version.environment">{environment}</span>
      <span data-testid="app.version.workbench">
        {environmentInfo?.workbench
          ? `${environmentInfo.workbench.mode} ${environmentInfo.workbench.version} / ${environmentInfo.workbench.previewId}`
          : 'Workbench unknown'}
      </span>
      <span data-testid="app.version.ui">UI {uiBuildInfo.version}</span>
      <span data-testid="app.version.branch">{uiBuildInfo.branch}</span>
      <span data-testid="app.version.commit">commit {uiBuildInfo.commitShort}</span>
      <span data-testid="app.version.api">API {apiStatus.status}</span>
      <code data-testid="app.version.apiHost">{apiHost}</code>
    </div>
  );
}

function getApiHost(baseUrl: string) {
  try {
    const url = new URL(baseUrl);
    return url.host || baseUrl;
  } catch {
    return baseUrl || 'unknown';
  }
}
