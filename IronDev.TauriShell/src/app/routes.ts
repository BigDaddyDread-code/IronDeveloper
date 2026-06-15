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

export type WorkspaceRouteId = 'home' | 'chat' | 'build' | 'tickets' | 'knowledge' | 'runs' | 'governance' | 'settings';

export interface WorkspaceRoute {
  id: WorkspaceRouteId;
  label: string;
  route: string;
  description: string;
}

export const workspaceRoutes: WorkspaceRoute[] = [
  {
    id: 'home',
    label: 'Home',
    route: '/',
    description: 'Project state, readiness, and suggested next actions.'
  },
  {
    id: 'chat',
    label: 'Chat',
    route: '/chat',
    description: 'Ask project-aware questions and inspect context used.'
  },
  {
    id: 'build',
    label: 'Build',
    route: '/build',
    description: 'Move discussion into review-only sandbox code runs and evidence.'
  },
  {
    id: 'tickets',
    label: 'Tickets',
    route: '/tickets',
    description: 'Plan, select, and inspect project work.'
  },
  {
    id: 'knowledge',
    label: 'Knowledge',
    route: '/knowledge',
    description: 'Documents, discussions, plans, decisions, and retrieval status.'
  },
  {
    id: 'runs',
    label: 'Runs',
    route: '/runs',
    description: 'Execution history, evidence, failures, and review packages.'
  },
  {
    id: 'governance',
    label: 'Governance',
    route: '/governance/timeline',
    description: 'Read-only governance trace timeline and operational evidence links.'
  },
  {
    id: 'settings',
    label: 'Settings',
    route: '/settings',
    description: 'Environment, API, service health, and local configuration.'
  }
];

export function routeForId(routeId: WorkspaceRoute['id']) {
  return workspaceRoutes.find((route) => route.id === routeId) ?? workspaceRoutes[0];
}

export function routeForPath(pathname: string) {
  const normalized = pathname.trim().replace(/\/+$/, '') || '/';

  return workspaceRoutes.find((route) => route.route === normalized) ?? workspaceRoutes[0];
}
