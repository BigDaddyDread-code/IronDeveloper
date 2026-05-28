import type { ReactNode } from 'react';

export interface MetadataGridItem {
  label: string;
  value: ReactNode;
}

interface MetadataGridProps {
  items: MetadataGridItem[];
  testId?: string;
}

export function MetadataGrid({ items, testId }: MetadataGridProps) {
  return (
    <dl className="metadata-grid" data-testid={testId}>
      {items.map((item) => (
        <div className="metadata-grid__item" key={item.label}>
          <dt>{item.label}</dt>
          <dd>{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}
