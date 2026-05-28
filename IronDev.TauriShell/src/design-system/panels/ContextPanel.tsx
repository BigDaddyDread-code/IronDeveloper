import type { ReactNode } from 'react';
import { SurfacePanel } from '../../components/SurfacePanel';

interface ContextPanelProps {
  title: string;
  eyebrow?: string;
  children: ReactNode;
  testId?: string;
  className?: string;
}

export function ContextPanel({ title, eyebrow = 'Context linked to this ticket', children, testId, className }: ContextPanelProps) {
  const classes = ['context-panel', className].filter(Boolean).join(' ');

  return (
    <SurfacePanel className={classes} testId={testId}>
      <div className="section-heading">
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
      </div>
      {children}
    </SurfacePanel>
  );
}
