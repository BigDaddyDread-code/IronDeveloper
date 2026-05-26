import type { ReactNode } from 'react';
import { WorkspaceLayout } from '../components/WorkspaceLayout';

interface SplitWorkspaceProps {
  left: ReactNode;
  center: ReactNode;
  right: ReactNode;
}

export function SplitWorkspace({ left, center, right }: SplitWorkspaceProps) {
  return <WorkspaceLayout left={left} center={center} right={right} />;
}

