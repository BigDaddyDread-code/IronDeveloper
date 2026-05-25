import type { ReactNode } from 'react';
import { SurfacePanel } from './SurfacePanel';

interface WorkspaceListPaneProps {
  eyebrow: string;
  title: string;
  children: ReactNode;
  testId: string;
}

export function WorkspaceListPane({ eyebrow, title, children, testId }: WorkspaceListPaneProps) {
  return (
    <SurfacePanel className="workspace-list-pane" testId={testId}>
      <div className="section-heading">
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
      </div>
      {children}
    </SurfacePanel>
  );
}
