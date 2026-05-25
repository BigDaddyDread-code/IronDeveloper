import type { ReactNode } from 'react';

interface WorkspaceShellProps {
  left: ReactNode;
  center: ReactNode;
  right: ReactNode;
}

export function WorkspaceShell({ left, center, right }: WorkspaceShellProps) {
  return (
    <section className="workspace-shell">
      <aside className="workspace-shell__left">{left}</aside>
      <section className="workspace-shell__center">{center}</section>
      <aside className="workspace-shell__right">{right}</aside>
    </section>
  );
}
