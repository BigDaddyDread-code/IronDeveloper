import type { ReactNode } from 'react';

export type WorkspaceCommandIntent = 'primary' | 'secondary' | 'danger' | 'ghost';

export interface WorkspaceCommand {
  id: string;
  label: string;
  intent: WorkspaceCommandIntent;
  icon?: ReactNode;
  shortcut?: string;
  disabled?: boolean;
  disabledReason?: string;
  busy?: boolean;
  onExecute: () => void;
  testId?: string;
}

export interface WorkspaceSummaryChip {
  label: string;
  testId?: string;
}

export interface WorkspaceRouteMeta {
  workspaceCommands: WorkspaceCommand[];
  workspaceBlockReason: string | null;
  workspaceSummaryChips: WorkspaceSummaryChip[];
  blockReasonTestId?: string;
}

export interface WorkspaceRoute {
  id: 'tickets' | 'run-reports' | 'promotion-review';
  label: string;
  route: string;
  maturity: 'spike' | 'alpha';
  parityStatus: 'spike' | 'alpha';
  parityNotes: string[];
}

export const workspaceRoutes: WorkspaceRoute[] = [
  {
    id: 'tickets',
    label: 'Tickets',
    route: 'tickets',
    maturity: 'spike',
    parityStatus: 'spike',
    parityNotes: [
      'Ticket lifecycle and project context are wired end-to-end.',
      'Evidence and run review surfaces are still alpha targets.'
    ]
  },
  {
    id: 'run-reports',
    label: 'Run Reports',
    route: 'run-reports',
    maturity: 'alpha',
    parityStatus: 'alpha',
    parityNotes: [
      'Primary evidence cockpit for governed runs.',
      'Shows timeline, evidence, and policy-aware inspection.'
    ]
  },
  {
    id: 'promotion-review',
    label: 'Promotion Review',
    route: 'promotion-review',
    maturity: 'alpha',
    parityStatus: 'alpha',
    parityNotes: [
      'Shows promotable vs blocked file groups.',
      'Exposes approval state and blocked action reasons before write operations.'
    ]
  }
];

export function routeForId(routeId: WorkspaceRoute['id']) {
  return workspaceRoutes.find((route) => route.id === routeId) ?? workspaceRoutes[0];
}
