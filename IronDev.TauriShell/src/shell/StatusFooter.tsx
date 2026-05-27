import type { ApiStatus } from '../api/types';
import { ApiStatusBadge } from '../components/ApiStatusBadge';

interface StatusFooterProps {
  apiStatus: ApiStatus;
}

export function StatusFooter({ apiStatus }: StatusFooterProps) {
  return (
    <footer className="shell-footer">
      <ApiStatusBadge status={apiStatus.status} withTestId={false} />
      <span>{apiStatus.message}</span>
    </footer>
  );
}
