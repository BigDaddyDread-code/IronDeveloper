import { expect, test } from '@playwright/test';
import { canonicalSurfaces, parseProductRoute } from '../src/flow/navigation/productRoutes';

test('canonical inventory names the complete product IA in stable order', () => {
  expect(canonicalSurfaces.map(({ id }) => id)).toEqual([
    'session',
    'projects',
    'board',
    'workItem',
    'library',
    'governance',
    'audit',
    'settings'
  ]);
  expect(canonicalSurfaces.filter(({ primary }) => primary).map(({ id }) => id)).toEqual([
    'board',
    'workItem',
    'library'
  ]);
});

test('inventory templates resolve to canonical routes and contain no compatibility aliases', () => {
  const concretePaths = canonicalSurfaces.map(({ routeTemplate }) =>
    routeTemplate.replace(':projectId', '42').replace(':workItemId', '7')
  );

  for (const pathname of concretePaths) {
    expect(parseProductRoute(pathname).compatibility, pathname).toBe(false);
  }

  expect(concretePaths).not.toEqual(expect.arrayContaining(['/chat', '/tickets', '/knowledge', '/runs', '/build']));
});
