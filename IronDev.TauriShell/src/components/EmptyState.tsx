import type { ReactNode } from 'react';

interface EmptyStateProps {
  title: string;
  body: string;
  action?: ReactNode;
}

export function EmptyState({ title, body, action }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <div className="section-heading">
        <p className="eyebrow">State</p>
        <h2>{title}</h2>
      </div>
      <p>{body}</p>
      {action}
    </div>
  );
}
