import { expect, test } from '@playwright/test';
import { legacyRouteAliases, parseProductRoute } from '../src/flow/navigation/productRoutes';

test('every legacy workspace alias is classified for redirect with notice', () => {
  expect(legacyRouteAliases).toHaveLength(8);
  expect(new Set(legacyRouteAliases.map(({ pattern }) => pattern)).size).toBe(legacyRouteAliases.length);
  expect(legacyRouteAliases.every(({ handling }) => handling === 'redirect-with-notice')).toBe(true);
});

test('legacy aliases remain compatibility routes rather than canonical routes', () => {
  for (const pathname of ['/chat', '/projects/42/chat', '/tickets', '/build', '/runs', '/batch', '/knowledge', '/settings']) {
    expect(parseProductRoute(pathname).compatibility, pathname).toBe(true);
  }
});
