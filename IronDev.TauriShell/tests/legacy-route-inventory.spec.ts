import { expect, test } from '@playwright/test';
import { legacyCanonicalPath, legacyRouteAliases, parseProductRoute } from '../src/flow/navigation/productRoutes';

test('every legacy workspace alias is classified for redirect with notice', () => {
  expect(legacyRouteAliases.map(({ pattern, canonicalSurface }) => ({ pattern, canonicalSurface }))).toEqual([
    { pattern: '/chat', canonicalSurface: 'workshop' },
    { pattern: '/projects/:projectId/chat[/...]', canonicalSurface: 'workshop' },
    { pattern: '/tickets', canonicalSurface: 'board' },
    { pattern: '/build', canonicalSurface: 'board' },
    { pattern: '/runs', canonicalSurface: 'board' },
    { pattern: '/batch', canonicalSurface: 'board' },
    { pattern: '/knowledge', canonicalSurface: 'library' },
    { pattern: '/settings', canonicalSurface: 'settings' }
  ]);
  expect(new Set(legacyRouteAliases.map(({ pattern }) => pattern)).size).toBe(legacyRouteAliases.length);
  expect(legacyRouteAliases.every(({ handling }) => handling === 'redirect-with-notice')).toBe(true);
});

test('legacy aliases remain compatibility routes rather than canonical routes', () => {
  const cases = [
    ['/chat', '/projects/7/workshop'],
    ['/projects/42/chat', '/projects/42/workshop'],
    ['/projects/42/chat/sessions/9', '/projects/42/workshop/sessions/9'],
    ['/projects/42/chat/channels/team%20alpha', '/projects/42/workshop/channels/team%20alpha'],
    ['/tickets', '/projects/7/board'],
    ['/build', '/projects/7/board'],
    ['/runs', '/projects/7/board'],
    ['/batch', '/projects/7/board'],
    ['/knowledge', '/projects/7/library'],
    ['/settings', '/projects/7/library/settings']
  ] as const;

  for (const [pathname, canonicalPath] of cases) {
    const route = parseProductRoute(pathname);
    expect(route.compatibility, pathname).toBe(true);
    expect(legacyCanonicalPath(route, 7), pathname).toBe(canonicalPath);
  }
});
