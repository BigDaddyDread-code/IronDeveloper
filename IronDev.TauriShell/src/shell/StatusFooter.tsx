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
    <footer className="shell-footer" data-testid="app.versionStrip">
      <ApiStatusBadge status={apiStatus.status} withTestId={false} />
      <span data-testid="app.version.environment">{environment}</span>
      <span data-testid="app.version.ui">UI {uiBuildInfo.version}</span>
      <span data-testid="app.version.branch">{uiBuildInfo.branch}</span>
      <span data-testid="app.version.commit">commit {uiBuildInfo.commitShort}</span>
      <span data-testid="app.version.api">API {apiStatus.status}</span>
      <code data-testid="app.version.apiHost">{apiHost}</code>
    </footer>
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
