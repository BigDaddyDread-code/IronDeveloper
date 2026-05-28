import type { ReactNode } from 'react';
import { SurfacePanel } from '../../components/SurfacePanel';

interface ReviewPanelProps {
  title: string;
  eyebrow?: string;
  actions?: ReactNode;
  children: ReactNode;
  testId?: string;
  className?: string;
}

export function ReviewPanel({ title, eyebrow = 'Run Review', actions, children, testId, className }: ReviewPanelProps) {
  const classes = ['review-panel', className].filter(Boolean).join(' ');

  return (
    <SurfacePanel className={classes} testId={testId}>
      <div className="review-panel__header">
        <div>
          <p className="eyebrow">{eyebrow}</p>
          <h3>{title}</h3>
        </div>
        {actions ? <div className="review-panel__actions">{actions}</div> : null}
      </div>
      {children}
    </SurfacePanel>
  );
}
