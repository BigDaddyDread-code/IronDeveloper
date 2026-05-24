import { defineConfig, devices } from '@playwright/test';

const baseUrl = process.env.IRONDEV_UI_BASE_URL ?? 'http://127.0.0.1:5173';

export default defineConfig({
  testDir: './tests',
  outputDir: './test-results',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 30_000,
  expect: {
    timeout: 5_000
  },
  reporter: [
    ['list'],
    ['json', { outputFile: 'reports/playwright-report.json' }],
    ['html', { outputFolder: 'reports/html', open: 'never' }]
  ],
  use: {
    baseURL: baseUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 10_000,
    navigationTimeout: 15_000
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
});
