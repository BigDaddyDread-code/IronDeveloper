import { useEffect, useState } from 'react';
import { isGovernancePath } from '../library/governanceRoutes';

export type LibrarySection =
  | 'explorer'
  | 'documents'
  | 'tools'
  | 'members'
  | 'governance'
  | 'provisioning'
  | 'audit'
  | 'settings';

export type ProductRouteKind =
  | 'root'
  | 'signIn'
  | 'tenantSelect'
  | 'projects'
  | 'projectConnect'
  | 'projectSetup'
  | 'board'
  | 'chat'
  | 'workItem'
  | 'library'
  | 'settings'
  | 'notFound';

export interface ProductRoute {
  kind: ProductRouteKind;
  pathname: string;
  projectId: number | null;
  chatSessionId: number | null;
  chatChannelId: string | null;
  workItemId: number | 'new' | null;
  librarySection: LibrarySection | null;
  compatibility: boolean;
}

const navigationEvent = 'irondev:navigation';

function route(
  pathname: string,
  kind: ProductRouteKind,
  options: Partial<Omit<ProductRoute, 'pathname' | 'kind'>> = {}
): ProductRoute {
  return {
    kind,
    pathname,
    projectId: options.projectId ?? null,
    chatSessionId: options.chatSessionId ?? null,
    chatChannelId: options.chatChannelId ?? null,
    workItemId: options.workItemId ?? null,
    librarySection: options.librarySection ?? null,
    compatibility: options.compatibility ?? false
  };
}

export function parseProductRoute(pathname: string): ProductRoute {
  const normalized = pathname.trim().replace(/\/+$/, '') || '/';

  if (normalized === '/') return route(normalized, 'root');
  if (normalized === '/sign-in') return route(normalized, 'signIn');
  if (normalized === '/tenants/select') return route(normalized, 'tenantSelect');
  if (normalized === '/projects') return route(normalized, 'projects');
  if (normalized === '/projects/connect') return route(normalized, 'projectConnect');

  if (isGovernancePath(normalized)) {
    return route(normalized, 'library', { librarySection: 'governance', compatibility: true });
  }

  const setupMatch = normalized.match(/^\/projects\/(\d+)\/setup$/);
  if (setupMatch) return route(normalized, 'projectSetup', { projectId: Number(setupMatch[1]) });

  const boardMatch = normalized.match(/^\/projects\/(\d+)\/board$/);
  if (boardMatch) return route(normalized, 'board', { projectId: Number(boardMatch[1]) });

  const chatSessionMatch = normalized.match(/^\/projects\/(\d+)\/chat\/sessions\/([1-9]\d*)$/);
  if (chatSessionMatch) {
    return route(normalized, 'chat', {
      projectId: Number(chatSessionMatch[1]),
      chatSessionId: Number(chatSessionMatch[2])
    });
  }

  const chatChannelMatch = normalized.match(/^\/projects\/(\d+)\/chat\/channels\/([^/]+)$/);
  if (chatChannelMatch) {
    return route(normalized, 'chat', {
      projectId: Number(chatChannelMatch[1]),
      chatChannelId: chatChannelMatch[2]
    });
  }

  const chatMatch = normalized.match(/^\/projects\/(\d+)\/chat$/);
  if (chatMatch) return route(normalized, 'chat', { projectId: Number(chatMatch[1]) });

  const workItemMatch = normalized.match(/^\/projects\/(\d+)\/work-items\/(new|\d+)$/);
  if (workItemMatch) {
    return route(normalized, 'workItem', {
      projectId: Number(workItemMatch[1]),
      workItemId: workItemMatch[2] === 'new' ? 'new' : Number(workItemMatch[2])
    });
  }

  const libraryMatch = normalized.match(/^\/projects\/(\d+)\/library(?:\/(explorer|documents|tools|members|governance|provisioning|audit|settings)(?:\/.*)?)?$/);
  if (libraryMatch) {
    return route(normalized, 'library', {
      projectId: Number(libraryMatch[1]),
      librarySection: (libraryMatch[2] as LibrarySection | undefined) ?? 'explorer'
    });
  }

  // Compatibility paths are resolved to the selected project and replaced with
  // the project-scoped URL by FlowShell. Governance evidence links stay intact.
  if (normalized === '/chat') return route(normalized, 'chat', { compatibility: true });
  if (normalized === '/settings') return route(normalized, 'settings', { compatibility: true });
  if (normalized === '/knowledge') {
    return route(normalized, 'library', { librarySection: 'explorer', compatibility: true });
  }
  if (normalized === '/runs' || normalized === '/batch' || normalized === '/tickets' || normalized === '/build') {
    return route(normalized, 'board', { compatibility: true });
  }

  return route(normalized, 'notFound');
}

export function projectPath(projectId: number, destination: 'setup' | 'board' | 'chat' | 'library'): string {
  return `/projects/${projectId}/${destination}`;
}

export function chatSessionPath(projectId: number, sessionId: number): string {
  return `${projectPath(projectId, 'chat')}/sessions/${sessionId}`;
}

export function workItemPath(projectId: number, workItemId: number | 'new'): string {
  return `/projects/${projectId}/work-items/${workItemId}`;
}

export function libraryPath(projectId: number, section: LibrarySection): string {
  return section === 'explorer'
    ? projectPath(projectId, 'library')
    : `${projectPath(projectId, 'library')}/${section}`;
}

export function navigateProductPath(pathname: string, replace = false): void {
  if (window.location.pathname === pathname) return;
  window.history[replace ? 'replaceState' : 'pushState'](null, '', pathname);
  window.dispatchEvent(new Event(navigationEvent));
}

export function useProductRoute(): ProductRoute {
  const [current, setCurrent] = useState(() => parseProductRoute(window.location.pathname));

  useEffect(() => {
    const update = () => setCurrent(parseProductRoute(window.location.pathname));
    window.addEventListener('popstate', update);
    window.addEventListener(navigationEvent, update);
    return () => {
      window.removeEventListener('popstate', update);
      window.removeEventListener(navigationEvent, update);
    };
  }, []);

  return current;
}
