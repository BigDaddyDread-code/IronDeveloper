import type { ReactNode } from 'react';

interface WorkspaceListItemProps {
  title: string;
  summary: string;
  isSelected: boolean;
  onSelect: () => void;
  badges?: ReactNode;
  footer?: ReactNode;
  testId: string;
}

export function WorkspaceListItem({ title, summary, isSelected, onSelect, badges, footer, testId }: WorkspaceListItemProps) {
  const classes = ['workspace-list-item', isSelected ? 'workspace-list-item--selected' : ''].filter(Boolean).join(' ');

  return (
    <button className={classes} data-testid={testId} onClick={onSelect}>
      <span className="workspace-list-item__rail" aria-hidden="true" />
      <span className="workspace-list-item__content">
        <span className="workspace-list-item__title">{title}</span>
        <span className="workspace-list-item__summary">{summary}</span>
        {badges ? <span className="workspace-list-item__badges">{badges}</span> : null}
        {footer ? <span className="workspace-list-item__footer">{footer}</span> : null}
      </span>
    </button>
  );
}
