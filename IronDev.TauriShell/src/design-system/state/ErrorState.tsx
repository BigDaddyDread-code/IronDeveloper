import type { ReactNode } from 'react';

interface ErrorStateProps {
  title: string;
  body: string;
  action?: ReactNode;
}

export function ErrorState({ title, body, action }: ErrorStateProps) {
  return (
    <div className="state-panel state-panel--error">
      <p className="eyebrow">Needs attention</p>
      <h3>{title}</h3>
      <p>{body}</p>
      {action}
    </div>
  );
}
