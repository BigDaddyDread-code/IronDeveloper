import { expect, test, type Page, type Route } from '@playwright/test';

test('Documents lists backend identities with current version truth and working filters', async ({ page }) => {
  await mockDocumentsWorkspace(page);
  await page.goto('/projects/7/library/documents');

  await expect(page.getByTestId('flow.documents.list')).toBeVisible();
  await expect(page.getByTestId('flow.documents.open.201')).toContainText('Architecture Direction');
  await expect(page.getByTestId('flow.documents.open.201')).toContainText('v1.0');
  await expect(page.getByTestId('flow.documents.open.201')).toContainText('Created in IronDev');
  await expect(page.getByTestId('flow.documents.open.201')).toContainText('Ready');
  await expect(page.getByTestId('flow.documents.open.202')).toContainText('Archived Decision');

  await page.getByTestId('flow.documents.search').fill('Architecture');
  await expect(page.getByTestId('flow.documents.open.201')).toBeVisible();
  await expect(page.getByTestId('flow.documents.open.202')).toHaveCount(0);

  await page.getByRole('button', { name: 'Archived', exact: true }).click();
  await expect(page.getByTestId('flow.documents.filteredEmpty')).toContainText('No documents match');
  await page.getByRole('button', { name: 'Reset filters', exact: true }).click();
  await expect(page.getByTestId('flow.documents.open.202')).toBeVisible();
});

test('document detail and immutable version URLs survive refresh', async ({ page }) => {
  await mockDocumentsWorkspace(page);
  await page.goto('/projects/7/library/documents');

  await page.getByTestId('flow.documents.open.201').click();
  await expect(page).toHaveURL('/projects/7/library/documents/201');
  await expect(page.getByTestId('flow.documents.detail')).toContainText('Architecture Direction');
  await expect(page.getByTestId('flow.documents.content')).toContainText('Current architecture');
  await page.getByText('Document details', { exact: true }).click();
  await expect(page.getByTestId('flow.documents.detail')).toContainText('architecture.md');
  await expect(page.getByTestId('flow.documents.detail')).toContainText('text/markdown');
  await expect(page.getByTestId('flow.documents.version.301')).toContainText('v0.1');

  await page.getByTestId('flow.documents.version.301').click();
  await expect(page).toHaveURL('/projects/7/library/documents/201/versions/301');
  await expect(page.getByTestId('flow.documents.content')).toContainText('Original architecture');

  await page.reload();
  await expect(page).toHaveURL('/projects/7/library/documents/201/versions/301');
  await expect(page.getByTestId('flow.documents.content')).toContainText('Original architecture');
});

test('an empty project has a distinct Documents state with a real safe action', async ({ page }) => {
  await mockDocumentsWorkspace(page, { documents: [] });
  await page.goto('/projects/7/library/documents');

  await expect(page.getByTestId('flow.documents.empty')).toContainText('No project documents');
  await expect(page.getByTestId('flow.documents.empty')).toContainText('Upload a Markdown or text file');
  await page.getByTestId('flow.documents.empty').getByRole('button', { name: 'Open Chat', exact: true }).click();
  await expect(page).toHaveURL('/projects/7/chat');
});

test('Documents list failure preserves the route and retries backend truth', async ({ page }) => {
  const state = await mockDocumentsWorkspace(page, { listFailures: 2 });
  await page.goto('/projects/7/library/documents');

  await expect(page.getByRole('heading', { name: 'Documents unavailable', exact: true })).toBeVisible();
  await expect(page).toHaveURL('/projects/7/library/documents');
  await page.getByTestId('flow.routeOutcome.primary').click();

  await expect(page.getByTestId('flow.documents.open.201')).toBeVisible();
  expect(state.listRequests).toBe(3);
});

test('a permission refusal does not disclose document identities', async ({ page }) => {
  await mockDocumentsWorkspace(page, { listErrorStatus: 403 });
  await page.goto('/projects/7/library/documents');

  await expect(page.getByRole('heading', { name: 'Documents access denied', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('403');
  await expect(page.getByTestId('flow.routeOutcome')).toContainText('Document access was refused.');
  await expect(page.getByTestId('flow.documents.open.201')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText('Architecture Direction');
});

test('missing documents and cross-document version IDs return honest 404 states', async ({ page }) => {
  await mockDocumentsWorkspace(page);
  await page.goto('/projects/7/library/documents/999');

  await expect(page.getByRole('heading', { name: 'Document not found', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');

  await page.goto('/projects/7/library/documents/201/versions/999');
  await expect(page.getByRole('heading', { name: 'Document version not found', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await expect(page.locator('body')).not.toContainText('Archived decision content');
});

test('upload stores a backend-owned immutable Draft and opens its canonical document route', async ({ page }) => {
  const state = await mockDocumentsWorkspace(page);
  await page.goto('/projects/7/library/documents/upload');

  await page.getByTestId('flow.documents.upload.file').setInputFiles({
    name: 'api-boundaries.md',
    mimeType: 'text/markdown',
    buffer: Buffer.from('# API boundaries\n\nKeep the client thin.')
  });
  await expect(page.getByTestId('flow.documents.upload.displayName')).toHaveValue('api boundaries');
  await page.getByTestId('flow.documents.upload.displayName').fill('API Boundary Notes');
  await page.getByTestId('flow.documents.upload.documentType').selectOption('Architecture');
  await page.getByTestId('flow.documents.upload.description').fill('Uploaded architecture context.');
  await page.getByTestId('flow.documents.upload.submit').click();

  await expect(page.getByTestId('flow.documents.upload.success')).toContainText('Document uploaded as Draft');
  await expect(page.getByTestId('flow.documents.upload.success')).toContainText('immutable v0.1');
  await expect(page.getByTestId('flow.documents.upload.success')).toContainText('not attached to Chat');
  await expect(page.getByTestId('flow.documents.upload.success')).not.toContainText('Ready');
  expect(state.uploadRequests).toBe(1);
  expect(state.lastUploadBody).toContain('API Boundary Notes');
  expect(state.lastUploadBody).toContain('Architecture');

  await page.getByRole('button', { name: 'Open document', exact: true }).click();
  await expect(page).toHaveURL('/projects/7/library/documents/203');
  await expect(page.getByTestId('flow.documents.content')).toContainText('Keep the client thin');
});

test('backend upload refusal preserves the selected file and entered metadata', async ({ page }) => {
  const state = await mockDocumentsWorkspace(page, { uploadErrorStatus: 415 });
  await page.goto('/projects/7/library/documents/upload');

  const fileInput = page.getByTestId('flow.documents.upload.file');
  await fileInput.setInputFiles({ name: 'notes.md', mimeType: 'text/markdown', buffer: Buffer.from('# Notes') });
  await page.getByTestId('flow.documents.upload.displayName').fill('Preserved notes');
  await page.getByTestId('flow.documents.upload.documentType').selectOption('DecisionLog');
  await page.getByTestId('flow.documents.upload.description').fill('Keep this after refusal.');
  await page.getByTestId('flow.documents.upload.submit').click();

  await expect(page.getByRole('alert')).toContainText('The backend refused this document type.');
  await expect(page.getByTestId('flow.documents.upload.displayName')).toHaveValue('Preserved notes');
  await expect(page.getByTestId('flow.documents.upload.documentType')).toHaveValue('DecisionLog');
  await expect(page.getByTestId('flow.documents.upload.description')).toHaveValue('Keep this after refusal.');
  expect(await fileInput.evaluate((input: HTMLInputElement) => input.files?.[0]?.name)).toBe('notes.md');
  expect(state.uploadRequests).toBe(1);
  await expect(page.getByTestId('flow.documents.upload.success')).toHaveCount(0);
});

test.describe('narrow Documents', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('keeps labelled list fields and document content inside the viewport', async ({ page }) => {
    await mockDocumentsWorkspace(page);
    await page.goto('/projects/7/library/documents');

    await expect(page.getByTestId('flow.documents.open.201')).toContainText('Architecture Direction');
    await expect(page.getByTestId('flow.documents.open.201')).toContainText('Build Plan');
    await page.getByTestId('flow.documents.open.201').click();
    await expect(page.getByTestId('flow.documents.content')).toContainText('Current architecture');

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });

  test('keeps the document upload form inside the viewport', async ({ page }) => {
    await mockDocumentsWorkspace(page);
    await page.goto('/projects/7/library/documents/upload');
    await expect(page.getByTestId('flow.documents.upload')).toBeVisible();

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });
});

interface DocumentsMockOptions {
  documents?: Array<Record<string, unknown>>;
  listFailures?: number;
  listErrorStatus?: number;
  uploadErrorStatus?: number;
}

interface DocumentsMockState {
  listRequests: number;
  uploadRequests: number;
  lastUploadBody: string;
}

async function mockDocumentsWorkspace(page: Page, options: DocumentsMockOptions = {}): Promise<DocumentsMockState> {
  const state: DocumentsMockState = { listRequests: 0, uploadRequests: 0, lastUploadBody: '' };
  let failuresRemaining = options.listFailures ?? 0;
  const documents = options.documents ?? [
    {
      id: 201,
      tenantId: 3,
      projectId: 7,
      title: 'Architecture Direction',
      slug: 'architecture-direction',
      documentType: 'BuildPlan',
      currentVersionId: 302,
      status: 'Active',
      origin: 'CreatedInIronDev',
      processingStatus: 'Ready',
      description: 'Current backend and client boundaries.',
      visibility: 'Project',
      originalFileName: 'architecture.md',
      mediaType: 'text/markdown',
      byteSize: 4096,
      createdAtUtc: '2026-07-01T08:00:00Z',
      updatedAtUtc: '2026-07-10T08:00:00Z',
      createdBy: 'alice@irondev.local',
      updatedBy: 'bob@irondev.local'
    },
    {
      id: 202,
      tenantId: 3,
      projectId: 7,
      title: 'Archived Decision',
      slug: 'archived-decision',
      documentType: 'DecisionLog',
      currentVersionId: 401,
      status: 'Archived',
      origin: 'CreatedInIronDev',
      processingStatus: 'Superseded',
      visibility: 'Project',
      createdAtUtc: '2026-06-01T08:00:00Z',
      updatedAtUtc: '2026-06-02T08:00:00Z',
      createdBy: 'alice@irondev.local',
      updatedBy: 'alice@irondev.local'
    }
  ];
  const versions: Record<number, Array<Record<string, unknown>>> = {
    201: [
      {
        id: 302,
        documentId: 201,
        versionMajor: 1,
        versionMinor: 0,
        versionLabel: 'v1.0',
        contentMarkdown: '# Current architecture\n\nUse typed API boundaries.',
        changeSummary: 'Accepted current direction.',
        parentVersionId: 301,
        status: 'Draft',
        createdAtUtc: '2026-07-10T08:00:00Z',
        createdBy: 'bob@irondev.local'
      },
      {
        id: 301,
        documentId: 201,
        versionMajor: 0,
        versionMinor: 1,
        versionLabel: 'v0.1',
        contentMarkdown: '# Original architecture\n\nInitial proposal.',
        changeSummary: 'Initial version.',
        status: 'Superseded',
        createdAtUtc: '2026-07-01T08:00:00Z',
        createdBy: 'alice@irondev.local'
      }
    ],
    202: [
      {
        id: 401,
        documentId: 202,
        versionMajor: 1,
        versionMinor: 0,
        versionLabel: 'v1.0',
        contentMarkdown: '# Archived decision content',
        status: 'Archived',
        createdAtUtc: '2026-06-02T08:00:00Z',
        createdBy: 'alice@irondev.local'
      }
    ]
  };
  const uploadedDocument = {
    id: 203,
    tenantId: 3,
    projectId: 7,
    title: 'API Boundary Notes',
    slug: 'api-boundary-notes',
    documentType: 'Architecture',
    currentVersionId: 501,
    status: 'Active',
    origin: 'Uploaded',
    processingStatus: 'Draft',
    description: 'Uploaded architecture context.',
    visibility: 'Project',
    originalFileName: 'api-boundaries.md',
    mediaType: 'text/markdown',
    byteSize: 46,
    createdAtUtc: '2026-07-10T09:00:00Z',
    createdBy: 'bob@irondev.local'
  };
  const uploadedVersion = {
    id: 501,
    documentId: 203,
    versionMajor: 0,
    versionMinor: 1,
    versionLabel: 'v0.1',
    contentMarkdown: '# API boundaries\n\nKeep the client thin.',
    changeSummary: 'Uploaded from api-boundaries.md.',
    status: 'Draft',
    createdAtUtc: '2026-07-10T09:00:00Z',
    createdBy: 'bob@irondev.local'
  };
  versions[203] = [uploadedVersion];

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/environment', (route) =>
    json(route, { environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    json(route, { userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    json(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    json(route, [{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
  );
  await page.route('**/irondev-api/api/projects/7/select', (route) => json(route, { projectId: 7 }));
  await page.route('**/irondev-api/api/projects/7/tickets', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => json(route, []));

  await page.route('**/irondev-api/api/projects/7/documents/upload', (route) => {
    state.uploadRequests += 1;
    state.lastUploadBody = route.request().postData() ?? '';
    if (options.uploadErrorStatus) {
      return json(route, { error: 'The backend refused this document type.' }, options.uploadErrorStatus);
    }
    return json(route, {
      document: uploadedDocument,
      version: uploadedVersion,
      processingStatus: 'Draft',
      boundary: 'The uploaded file is an immutable Draft document. It is not attached to Chat, indexed for retrieval, approved, or source-mutation authority.'
    }, 201);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/documents(?:\?[^#]*)?$/, (route) => {
    state.listRequests += 1;
    if (options.listErrorStatus) {
      return json(route, { error: 'Document access was refused.' }, options.listErrorStatus);
    }
    if (failuresRemaining > 0) {
      failuresRemaining -= 1;
      return json(route, { error: 'Documents store unavailable.' }, 503);
    }
    return json(route, documents);
  });
  await page.route(/\/irondev-api\/api\/projects\/7\/documents\/(\d+)\/versions\/current$/, (route) => {
    const documentId = route.request().url().match(/documents\/(\d+)\/versions/)?.[1];
    const history = documentId ? versions[Number(documentId)] : undefined;
    return history?.[0] ? json(route, history[0]) : json(route, { error: 'Version not found.' }, 404);
  });
  await page.route(/\/irondev-api\/api\/projects\/7\/documents\/(\d+)\/versions$/, (route) => {
    const documentId = route.request().url().match(/documents\/(\d+)\/versions/)?.[1];
    const history = documentId ? versions[Number(documentId)] : undefined;
    return history ? json(route, history) : json(route, { error: 'Document not found.' }, 404);
  });
  await page.route(/\/irondev-api\/api\/projects\/7\/documents\/(\d+)$/, (route) => {
    const documentId = Number(route.request().url().match(/documents\/(\d+)$/)?.[1]);
    const document = documentId === 203 ? uploadedDocument : documents.find((candidate) => candidate.id === documentId);
    return document ? json(route, document) : json(route, { error: 'Document not found.' }, 404);
  });

  return state;
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
