import { expect, test } from '@playwright/test';
import { canonicalSurfaces, parseProductRoute } from '../src/flow/navigation/productRoutes';

test('canonical inventory names the complete product IA in stable order', () => {
  expect(canonicalSurfaces).toEqual([
    { id: 'session', label: 'Session / front door', routeTemplate: '/', primary: false, projectScoped: false },
    { id: 'projects', label: 'Project chooser', routeTemplate: '/projects', primary: false, projectScoped: false },
    { id: 'board', label: 'Board', routeTemplate: '/projects/:projectId/board', primary: true, projectScoped: true },
    { id: 'workItem', label: 'Work Item', routeTemplate: '/projects/:projectId/work-items/:workItemId', primary: true, projectScoped: true },
    { id: 'library', label: 'Library', routeTemplate: '/projects/:projectId/library', primary: true, projectScoped: true },
    { id: 'governance', label: 'Governance', routeTemplate: '/projects/:projectId/library/governance', primary: false, projectScoped: true },
    { id: 'audit', label: 'Audit', routeTemplate: '/projects/:projectId/library/audit', primary: false, projectScoped: true },
    { id: 'settings', label: 'Settings', routeTemplate: '/projects/:projectId/library/settings', primary: false, projectScoped: true }
  ]);
  expect(canonicalSurfaces.filter(({ primary }) => primary).map(({ id }) => id)).toEqual([
    'board',
    'workItem',
    'library'
  ]);
});

test('inventory templates resolve to canonical routes and contain no compatibility aliases', () => {
  const expectedKinds = {
    session: 'root',
    projects: 'projects',
    board: 'board',
    workItem: 'workItem',
    library: 'library',
    governance: 'library',
    audit: 'library',
    settings: 'library'
  } as const;
  const concretePaths = canonicalSurfaces.map((surface) => ({
    surface,
    pathname: surface.routeTemplate.replace(':projectId', '42').replace(':workItemId', '7')
  }));

  for (const { surface, pathname } of concretePaths) {
    const parsed = parseProductRoute(pathname);
    expect(parsed.kind, pathname).toBe(expectedKinds[surface.id]);
    expect(parsed.compatibility, pathname).toBe(false);
    expect(parsed.projectId, pathname).toBe(surface.projectScoped ? 42 : null);
  }
  expect(parseProductRoute('/projects/42/work-items/7').workItemId).toBe(7);

  const compatibilityAliases = ['/chat', '/settings', '/knowledge', '/runs', '/batch', '/tickets', '/build'];
  expect(concretePaths.map(({ pathname }) => pathname)).not.toEqual(expect.arrayContaining(compatibilityAliases));
  for (const alias of compatibilityAliases) {
    expect(parseProductRoute(alias).compatibility, alias).toBe(true);
  }
});
