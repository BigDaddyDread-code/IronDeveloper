import type { ReactNode } from 'react';
import type { ApiConnectionStatus } from '../api/types';

type BadgeStatus = ApiConnectionStatus | 'neutral' | 'info';

interface StatusBadgeProps {
  status: BadgeStatus;
  children: ReactNode;
  'data-testid'?: string;
}

export function StatusBadge({ status, children, 'data-testid': testId }: StatusBadgeProps) {
  return (
    <span className={`status-badge status-badge--${status}`} data-testid={testId}>
      {children}
    </span>
  );
}
