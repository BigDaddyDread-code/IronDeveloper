import type { ReactNode } from 'react';
import type { WorkspaceCommand } from '../../app/routes';
import { WorkspaceCommandBar } from './WorkspaceCommandBar';

interface WorkspaceFrameProps {
  title: string;
  description: string;
  metadata?: ReactNode;
  commands?: WorkspaceCommand[];
  children: ReactNode;
  reviewPanel?: ReactNode;
  testId?: string;
}

export function WorkspaceFrame({
  title,
  description,
  metadata,
  commands = [],
  children,
  reviewPanel,
  testId
}: WorkspaceFrameProps) {
  return (
    <main className="workspace-frame" data-testid={testId}>
      <header className="workspace-frame__header">
        <div className="workspace-frame__identity">
          <p className="eyebrow">Workspace</p>
          <h2>{title}</h2>
          <p>{description}</p>
          {metadata ? <div className="workspace-frame__metadata">{metadata}</div> : null}
        </div>
        <WorkspaceCommandBar commands={commands} />
      </header>
      {children}
      {reviewPanel ? <div className="workspace-frame__review">{reviewPanel}</div> : null}
    </main>
  );
}
