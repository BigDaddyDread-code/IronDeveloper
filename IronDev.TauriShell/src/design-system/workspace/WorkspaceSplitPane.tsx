import type { ReactNode } from 'react';

interface WorkspaceSplitPaneProps {
  list: ReactNode;
  detail: ReactNode;
  context: ReactNode;
}

export function WorkspaceSplitPane({ list, detail, context }: WorkspaceSplitPaneProps) {
  return (
    <section className="workspace-split-pane">
      <aside className="workspace-split-pane__list">{list}</aside>
      <section className="workspace-split-pane__detail">{detail}</section>
      <aside className="workspace-split-pane__context">{context}</aside>
    </section>
  );
}
