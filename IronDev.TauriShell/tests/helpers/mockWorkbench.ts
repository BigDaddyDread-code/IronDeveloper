import type { Page, Route } from '@playwright/test';

type ProjectLifecyclePhase = 'Shaping' | 'Delivery' | 'Archived';
type ExecutionReadiness = 'NotConfigured' | 'ValidationRequired' | 'Ready';

export interface WorkbenchProjectOpenFixture {
  tenantId?: number;
  name?: string;
  projectLifecyclePhase?: ProjectLifecyclePhase;
  executionReadiness?: ExecutionReadiness;
  repositoryBinding?: Record<string, unknown> | null;
  workbenchSessionId?: number;
  leaseEpoch?: number;
  wasResumed?: boolean;
  wasTakenOver?: boolean;
}

export async function mockWorkbenchProjectOpen(
  page: Page,
  projectId: number,
  fixture: WorkbenchProjectOpenFixture = {}
): Promise<void> {
  await page.route(`**/irondev-api/api/workbench/projects/${projectId}/open`, (route) =>
    fulfillWorkbenchProjectOpen(route, projectId, fixture)
  );
}

export function fulfillWorkbenchProjectOpen(
  route: Route,
  projectId: number,
  fixture: WorkbenchProjectOpenFixture = {}
): Promise<void> {
  return route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(workbenchProjectEntryContext(route, projectId, fixture))
  });
}

export function workbenchProjectEntryContext(
  route: Route,
  projectId: number,
  fixture: WorkbenchProjectOpenFixture = {}
): Record<string, unknown> {
  const request = route.request().postDataJSON() as { clientOperationId?: unknown; takeOver?: unknown };
  if (typeof request.clientOperationId !== 'string' || request.clientOperationId.length === 0) {
    throw new Error('The Workbench-open fixture requires the client operation id from the request.');
  }

  return {
    projectId,
    tenantId: fixture.tenantId ?? 3,
    name: fixture.name ?? (projectId === 7 ? 'BookSeller' : `Project ${projectId}`),
    projectLifecyclePhase: fixture.projectLifecyclePhase ?? 'Shaping',
    executionReadiness: fixture.executionReadiness ?? 'NotConfigured',
    repositoryBinding: fixture.repositoryBinding ?? null,
    workbenchSessionId: fixture.workbenchSessionId ?? projectId * 1001,
    leaseEpoch: fixture.leaseEpoch ?? 1,
    wasResumed: fixture.wasResumed ?? true,
    wasTakenOver: fixture.wasTakenOver ?? request.takeOver === true,
    clientOperationId: request.clientOperationId
  };
}

export async function mockLocalTestPreflight(page: Page): Promise<void> {
  await page.route('**/irondev-api/api/localtest/preflight', (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      state: 'LocalTestReady',
      environment: 'LocalTest',
      database: 'IronDeveloper_Test',
      apiBuildCommit: 'test-commit',
      launcherRepositoryCommit: 'test-commit',
      apiBaseUrl: 'http://localhost:5000',
      sessionMode: 'SmokeSimulation',
      sandboxApplyRequested: false,
      sandboxApplyEnabled: false,
      sandboxApplyRoot: null,
      capabilities: []
    })
  }));
}
