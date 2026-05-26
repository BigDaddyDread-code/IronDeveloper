import type { ReactNode } from 'react';
import { SurfacePanel } from '../components/SurfacePanel';

interface InspectorPanelProps {
  children: ReactNode;
  testId?: string;
}

export function InspectorPanel({ children, testId }: InspectorPanelProps) {
  return (
    <SurfacePanel className="inspector-panel" testId={testId}>
      {children}
    </SurfacePanel>
  );
}

