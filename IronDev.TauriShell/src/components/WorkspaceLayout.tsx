import type { ReactNode } from 'react';

interface WorkspaceLayoutProps {
  left: ReactNode;
  center: ReactNode;
  right: ReactNode;
}

export function WorkspaceLayout({ left, center, right }: WorkspaceLayoutProps) {
  return (
    <section className="workspace-layout">
      <aside className="workspace-layout__left">{left}</aside>
      <section className="workspace-layout__center">{center}</section>
      <aside className="workspace-layout__right">{right}</aside>
    </section>
  );
}
