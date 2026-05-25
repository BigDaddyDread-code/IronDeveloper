import type { ReactNode } from 'react';

interface SurfacePanelProps {
  children: ReactNode;
  className?: string;
  testId?: string;
}

export function SurfacePanel({ children, className, testId }: SurfacePanelProps) {
  const classes = ['surface-panel', className].filter(Boolean).join(' ');

  return (
    <section className={classes} data-testid={testId}>
      {children}
    </section>
  );
}
