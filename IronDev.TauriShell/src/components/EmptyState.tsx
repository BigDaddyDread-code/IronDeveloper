import type { ReactNode } from 'react';
import { TruthStateRenderer } from '../design-system/state/TruthStateRenderer';

interface EmptyStateProps {
  title: string;
  body: string;
  action?: ReactNode;
}

export function EmptyState({ title, body, action }: EmptyStateProps) {
  return <TruthStateRenderer kind="empty" title={title} body={body} action={action} className="empty-state" headingLevel={2} />;
}
