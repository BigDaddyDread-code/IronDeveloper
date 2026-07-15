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

export type GovernanceSection = 'overview' | 'controls' | 'exceptions' | 'decisions' | 'technical';

export interface LegacyRouteAlias {
  pattern: string;
  canonicalSurface: 'board' | 'workshop' | 'library' | 'settings';
  handling: 'redirect-with-notice';
}

export const legacyRouteAliases: readonly LegacyRouteAlias[] = [
  { pattern: '/chat', canonicalSurface: 'workshop', handling: 'redirect-with-notice' },
  { pattern: '/projects/:projectId/chat[/...]', canonicalSurface: 'workshop', handling: 'redirect-with-notice' },
  { pattern: '/tickets', canonicalSurface: 'board', handling: 'redirect-with-notice' },
  { pattern: '/build', canonicalSurface: 'board', handling: 'redirect-with-notice' },
  { pattern: '/runs', canonicalSurface: 'board', handling: 'redirect-with-notice' },
  { pattern: '/batch', canonicalSurface: 'board', handling: 'redirect-with-notice' },
  { pattern: '/knowledge', canonicalSurface: 'library', handling: 'redirect-with-notice' },
  { pattern: '/settings', canonicalSurface: 'settings', handling: 'redirect-with-notice' }
] as const;

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

export type CanonicalSurfaceId =
  | 'session'
  | 'projects'
  | 'board'
  | 'workItem'
  | 'library'
  | 'governance'
  | 'audit'
  | 'settings';

export interface CanonicalSurface {
  id: CanonicalSurfaceId;
  label: string;
  routeTemplate: string;
  primary: boolean;
  projectScoped: boolean;
}

/**
 * The product information architecture, in entry-to-work order. This is the
 * navigation contract; compatibility paths are deliberately absent.
 */
export const canonicalSurfaces: readonly CanonicalSurface[] = [
  { id: 'session', label: 'Session / front door', routeTemplate: '/', primary: false, projectScoped: false },
  { id: 'projects', label: 'Project chooser', routeTemplate: '/projects', primary: false, projectScoped: false },
  { id: 'board', label: 'Board', routeTemplate: '/projects/:projectId/board', primary: true, projectScoped: true },
  { id: 'workItem', label: 'Work Item', routeTemplate: '/projects/:projectId/work-items/:workItemId', primary: true, projectScoped: true },
  { id: 'library', label: 'Library', routeTemplate: '/projects/:projectId/library', primary: true, projectScoped: true },
  { id: 'governance', label: 'Governance', routeTemplate: '/projects/:projectId/library/governance', primary: false, projectScoped: true },
  { id: 'audit', label: 'Audit', routeTemplate: '/projects/:projectId/library/audit', primary: false, projectScoped: true },
  { id: 'settings', label: 'Settings', routeTemplate: '/projects/:projectId/library/settings', primary: false, projectScoped: true }
] as const;

export interface ProductRoute {
  kind: ProductRouteKind;
  pathname: string;
  projectId: number | null;
  chatSessionId: number | null;
  chatChannelId: string | null;
  workItemId: number | 'new' | null;
  librarySection: LibrarySection | null;
  governanceSection: GovernanceSection | null;
  libraryDocumentId: number | null;
  libraryDocumentVersionId: number | null;
  libraryDocumentAction: 'upload' | null;
  libraryToolId: string | null;
  libraryAuditLedgerId: string | null;
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
    governanceSection: options.governanceSection ?? null,
    libraryDocumentId: options.libraryDocumentId ?? null,
    libraryDocumentVersionId: options.libraryDocumentVersionId ?? null,
    libraryDocumentAction: options.libraryDocumentAction ?? null,
    libraryToolId: options.libraryToolId ?? null,
    libraryAuditLedgerId: options.libraryAuditLedgerId ?? null,
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

  const workshopSessionMatch = normalized.match(/^\/projects\/(\d+)\/workshop\/sessions\/([1-9]\d*)$/);
  if (workshopSessionMatch) {
    return route(normalized, 'chat', {
      projectId: Number(workshopSessionMatch[1]),
      chatSessionId: Number(workshopSessionMatch[2])
    });
  }

  const workshopChannelMatch = normalized.match(/^\/projects\/(\d+)\/workshop\/channels\/([^/]+)$/);
  if (workshopChannelMatch) {
    return route(normalized, 'chat', {
      projectId: Number(workshopChannelMatch[1]),
      chatChannelId: decodeURIComponent(workshopChannelMatch[2])
    });
  }

  const workshopMatch = normalized.match(/^\/projects\/(\d+)\/workshop$/);
  if (workshopMatch) return route(normalized, 'chat', { projectId: Number(workshopMatch[1]) });

  const chatSessionMatch = normalized.match(/^\/projects\/(\d+)\/chat\/sessions\/([1-9]\d*)$/);
  if (chatSessionMatch) {
    return route(normalized, 'chat', {
      projectId: Number(chatSessionMatch[1]),
      chatSessionId: Number(chatSessionMatch[2]),
      compatibility: true
    });
  }

  const chatChannelMatch = normalized.match(/^\/projects\/(\d+)\/chat\/channels\/([^/]+)$/);
  if (chatChannelMatch) {
    return route(normalized, 'chat', {
      projectId: Number(chatChannelMatch[1]),
      chatChannelId: decodeURIComponent(chatChannelMatch[2]),
      compatibility: true
    });
  }

  const chatMatch = normalized.match(/^\/projects\/(\d+)\/chat$/);
  if (chatMatch) return route(normalized, 'chat', { projectId: Number(chatMatch[1]), compatibility: true });

  const workItemMatch = normalized.match(/^\/projects\/(\d+)\/work-items\/(new|\d+)$/);
  if (workItemMatch) {
    return route(normalized, 'workItem', {
      projectId: Number(workItemMatch[1]),
      workItemId: workItemMatch[2] === 'new' ? 'new' : Number(workItemMatch[2])
    });
  }

  const documentUploadMatch = normalized.match(/^\/projects\/(\d+)\/library\/documents\/upload$/);
  if (documentUploadMatch) {
    return route(normalized, 'library', {
      projectId: Number(documentUploadMatch[1]),
      librarySection: 'documents',
      libraryDocumentAction: 'upload'
    });
  }

  const documentVersionMatch = normalized.match(
    /^\/projects\/(\d+)\/library\/documents\/([1-9]\d*)\/versions\/([1-9]\d*)$/
  );
  if (documentVersionMatch) {
    return route(normalized, 'library', {
      projectId: Number(documentVersionMatch[1]),
      librarySection: 'documents',
      libraryDocumentId: Number(documentVersionMatch[2]),
      libraryDocumentVersionId: Number(documentVersionMatch[3])
    });
  }

  const documentDetailMatch = normalized.match(/^\/projects\/(\d+)\/library\/documents\/([1-9]\d*)$/);
  if (documentDetailMatch) {
    return route(normalized, 'library', {
      projectId: Number(documentDetailMatch[1]),
      librarySection: 'documents',
      libraryDocumentId: Number(documentDetailMatch[2])
    });
  }

  const libraryGovernanceMatch = normalized.match(
    /^\/projects\/(\d+)\/library\/governance(?:\/(controls|exceptions|decisions|technical))?$/
  );
  if (libraryGovernanceMatch) {
    return route(normalized, 'library', {
      projectId: Number(libraryGovernanceMatch[1]),
      librarySection: 'governance',
      governanceSection: (libraryGovernanceMatch[2] as GovernanceSection | undefined) ?? 'overview'
    });
  }

  const toolDetailMatch = normalized.match(/^\/projects\/(\d+)\/library\/tools\/([^/]+)$/);
  if (toolDetailMatch) {
    return route(normalized, 'library', {
      projectId: Number(toolDetailMatch[1]),
      librarySection: 'tools',
      libraryToolId: decodeURIComponent(toolDetailMatch[2])
    });
  }

  const auditEventMatch = normalized.match(/^\/projects\/(\d+)\/library\/audit\/events\/([^/]+)$/);
  if (auditEventMatch) {
    return route(normalized, 'library', {
      projectId: Number(auditEventMatch[1]),
      librarySection: 'audit',
      libraryAuditLedgerId: decodeURIComponent(auditEventMatch[2])
    });
  }

  const libraryMatch = normalized.match(/^\/projects\/(\d+)\/library(?:\/(explorer|documents|tools|members|provisioning|audit|settings))?$/);
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

export function safeProjectProductPath(value: string | null | undefined, projectId: number): string | null {
  const candidate = value?.trim();
  if (!candidate?.startsWith('/') || candidate.startsWith('//')) return null;

  const url = new URL(candidate, window.location.origin);
  if (url.origin !== window.location.origin) return null;
  const route = parseProductRoute(url.pathname);
  if (route.kind === 'notFound' || route.projectId !== projectId) return null;
  return `${url.pathname}${url.search}${url.hash}`;
}

export function projectPath(projectId: number, destination: 'setup' | 'board' | 'chat' | 'library'): string {
  const canonicalDestination = destination === 'chat' ? 'workshop' : destination;
  return `/projects/${projectId}/${canonicalDestination}`;
}

export function chatSessionPath(projectId: number, sessionId: number): string {
  return `${projectPath(projectId, 'chat')}/sessions/${sessionId}`;
}

export function chatChannelPath(projectId: number, channelReference: string): string {
  return `${projectPath(projectId, 'chat')}/channels/${encodeURIComponent(channelReference)}`;
}

export function workItemPath(projectId: number, workItemId: number | 'new'): string {
  return `/projects/${projectId}/work-items/${workItemId}`;
}

export function libraryPath(projectId: number, section: LibrarySection): string {
  return section === 'explorer'
    ? projectPath(projectId, 'library')
    : `${projectPath(projectId, 'library')}/${section}`;
}

export function governancePath(projectId: number, section: GovernanceSection = 'overview'): string {
  const root = libraryPath(projectId, 'governance');
  return section === 'overview' ? root : `${root}/${section}`;
}

export function documentPath(projectId: number, documentId: number): string {
  return `${libraryPath(projectId, 'documents')}/${documentId}`;
}

export function documentUploadPath(projectId: number): string {
  return `${libraryPath(projectId, 'documents')}/upload`;
}

export function documentVersionPath(projectId: number, documentId: number, versionId: number): string {
  return `${documentPath(projectId, documentId)}/versions/${versionId}`;
}

export function toolPath(projectId: number, toolId: string): string {
  return `${libraryPath(projectId, 'tools')}/${encodeURIComponent(toolId)}`;
}

export function auditEventPath(projectId: number, ledgerId: string): string {
  return `${libraryPath(projectId, 'audit')}/events/${encodeURIComponent(ledgerId)}`;
}

export function legacyCanonicalPath(route: ProductRoute, selectedProjectId: number): string | null {
  if (!route.compatibility || route.librarySection === 'governance') return null;

  if (route.kind === 'chat') {
    const projectId = route.projectId ?? selectedProjectId;
    if (route.chatSessionId !== null) return chatSessionPath(projectId, route.chatSessionId);
    if (route.chatChannelId !== null) return chatChannelPath(projectId, route.chatChannelId);
    return projectPath(projectId, 'chat');
  }
  if (route.kind === 'board') return projectPath(selectedProjectId, 'board');
  if (route.kind === 'settings') return libraryPath(selectedProjectId, 'settings');
  if (route.kind === 'library' && route.pathname === '/knowledge') return projectPath(selectedProjectId, 'library');
  return null;
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
