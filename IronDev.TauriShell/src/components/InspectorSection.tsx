import type { ReactNode } from 'react';

interface InspectorSectionProps {
  title: string;
  children: ReactNode;
  testId?: string;
}

export function InspectorSection({ title, children, testId }: InspectorSectionProps) {
  return (
    <section className="inspector-section" data-testid={testId}>
      <h3>{title}</h3>
      <div className="inspector-section__body">{children}</div>
    </section>
  );
}
