import type { ReactNode } from 'react';
import { SurfacePanel } from '../components/SurfacePanel';

interface SurfaceProps {
  children: ReactNode;
  testId?: string;
  className?: string;
}

export function Surface({ children, testId, className }: SurfaceProps) {
  return (
    <SurfacePanel className={className} testId={testId}>
      {children}
    </SurfacePanel>
  );
}

