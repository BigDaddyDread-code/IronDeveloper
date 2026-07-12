import { expect, test } from '@playwright/test';
import { governanceViewers, viewerForPath } from '../src/flow/library/governanceRoutes';
import { governancePath, parseProductRoute } from '../src/flow/navigation/productRoutes';

test('governance compatibility inventory classifies all 17 viewers without changing deep-link resolution', () => {
  expect(governanceViewers).toHaveLength(17);

  for (const viewer of governanceViewers) {
    expect(viewer.canonicalOwner).toBeTruthy();
    expect(viewer.disposition).toBeTruthy();
    expect(viewerForPath(viewer.entryPath).id).toBe(viewer.id);
  }
});

test('canonical project governance routes resolve to the Governance library surface', () => {
  const sections = ['overview', 'controls', 'exceptions', 'decisions', 'technical'] as const;

  for (const section of sections) {
    const pathname = governancePath(42, section);
    const route = parseProductRoute(pathname);

    expect(route.kind).toBe('library');
    expect(route.projectId).toBe(42);
    expect(route.librarySection).toBe('governance');
    expect(route.governanceSection).toBe(section);
    expect(route.compatibility).toBe(false);
  }
});

test('unknown project Governance subsections do not silently become technical evidence', () => {
  const route = parseProductRoute('/projects/42/library/governance/internal-record-name');

  expect(route.kind).toBe('notFound');
  expect(route.librarySection).toBeNull();
  expect(route.governanceSection).toBeNull();
});
