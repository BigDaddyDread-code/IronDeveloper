import type { ReactNode } from 'react';
import { TruthStateRenderer } from './TruthStateRenderer';

interface ErrorStateProps {
  title: string;
  body: string;
  action?: ReactNode;
}

export function ErrorState({ title, body, action }: ErrorStateProps) {
  return (
    <TruthStateRenderer
      kind="error"
      title={title}
      body={body}
      action={action}
      className="state-panel state-panel--error"
    />
  );
}
