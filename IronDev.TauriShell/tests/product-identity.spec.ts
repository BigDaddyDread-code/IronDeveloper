import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';

test('desktop metadata is one stable IronDev identity', async () => {
  const packageJson = JSON.parse(await readFile('package.json', 'utf8')) as { name: string; version: string };
  const tauri = JSON.parse(await readFile('src-tauri/tauri.conf.json', 'utf8')) as {
    productName: string;
    version: string;
    identifier: string;
    bundle: { icon: string[] };
  };
  const cargo = await readFile('src-tauri/Cargo.toml', 'utf8');
  const html = await readFile('index.html', 'utf8');

  expect(packageJson).toMatchObject({ name: 'irondev-desktop', version: '0.5.0' });
  expect(tauri).toMatchObject({ productName: 'IronDev', version: '0.5.0', identifier: 'com.irondeveloper.irondev' });
  expect(cargo).toContain('name = "irondev-desktop"');
  expect(cargo).toContain('version = "0.5.0"');
  expect(html).toContain('<title>IronDev</title>');
  expect(html).not.toContain('Shell Spike');
  expect(tauri.bundle.icon).toEqual(expect.arrayContaining([
    'icons/16x16.png',
    'icons/32x32.png',
    'icons/48x48.png',
    'icons/128x128.png',
    'icons/256x256.png',
    'icons/512x512.png',
    'icons/icon.ico',
    'icons/icon.icns'
  ]));
});

test('sign in presents the IronDev mark and governed engineering descriptor', async ({ page }) => {
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) });
  });
  await page.route('**/irondev-api/api/environment', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true }) });
  });

  await page.goto('/sign-in');

  await expect(page).toHaveTitle('IronDev');
  await expect(page.locator('.fl-brand-mark')).toBeVisible();
  await expect(page.getByText('Governed engineering', { exact: true })).toBeVisible();
  await expect(page.locator('link[rel="icon"]')).toHaveAttribute('href', '/favicon.svg');
});
