import type { ApiConnectionStatus } from '../api/types';
import { StatusBadge } from './StatusBadge';

interface ApiStatusBadgeProps {
  status: ApiConnectionStatus;
  withTestId?: boolean;
}

const labels: Record<ApiConnectionStatus, string> = {
  loading: 'Loading',
  connected: 'Connected',
  disconnected: 'Disconnected',
  authRequired: 'Auth required',
  error: 'Error'
};

export function ApiStatusBadge({ status, withTestId = true }: ApiStatusBadgeProps) {
  return (
    <StatusBadge status={status} data-testid={withTestId ? `api.status.${status}` : undefined}>
      {labels[status]}
    </StatusBadge>
  );
}
