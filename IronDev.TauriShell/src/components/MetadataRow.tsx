import type { ReactNode } from 'react';

interface MetadataRowProps {
  label: string;
  value: ReactNode;
}

export function MetadataRow({ label, value }: MetadataRowProps) {
  return (
    <dl className="metadata-row">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </dl>
  );
}
