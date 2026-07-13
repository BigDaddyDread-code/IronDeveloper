import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import { parseProductRoute } from '../src/flow/navigation/productRoutes';

const deletedLegacyModules = [
  'src/features/home/HomeRoute.tsx',
  'src/features/chatToBuild/BuildRoute.tsx',
  'src/features/runReports/RunReportsRoute.tsx',
  'src/features/runReports/PromotionReviewRoute.tsx',
  'src/features/runReports/useRunReportsWorkspace.ts'
];

test('proved legacy route modules stay deleted', () => {
  for (const relativePath of deletedLegacyModules) {
    expect(existsSync(resolve(relativePath)), relativePath).toBe(false);
  }

  const liveProviders = readFileSync(resolve('src/app/AppProviders.tsx'), 'utf8');
  expect(liveProviders).not.toContain('WorkspaceNavigationProvider');
});

test('canonical replacements remain routable without legacy workspace routes', () => {
  expect(parseProductRoute('/projects/7/board')).toMatchObject({ kind: 'board', projectId: 7, compatibility: false });
  expect(parseProductRoute('/projects/7/work-items/42')).toMatchObject({ kind: 'workItem', projectId: 7, compatibility: false });
  expect(parseProductRoute('/projects/7/library/audit')).toMatchObject({
    kind: 'library',
    projectId: 7,
    librarySection: 'audit',
    compatibility: false
  });
  expect(parseProductRoute('/build').compatibility).toBe(true);
  expect(parseProductRoute('/runs').compatibility).toBe(true);
});
