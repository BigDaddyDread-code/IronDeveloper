import type { ReactNode } from 'react';

interface EvidenceCardProps {
  title: string;
  children: ReactNode;
}

export function EvidenceCard({ title, children }: EvidenceCardProps) {
  return (
    <article className="evidence-card">
      <h3>{title}</h3>
      {children}
    </article>
  );
}
