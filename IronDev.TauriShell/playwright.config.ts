import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.IRONDEV_TAURI_SHELL_BASE_URL ?? 'http://127.0.0.1:5173';
const webServerCommand = process.env.CI
  ? 'npm run preview:playwright'
  : 'npm run dev';

export default defineConfig({
  testDir: './tests',
  outputDir: './test-results',
  fullyParallel: false,
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
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 10_000,
    navigationTimeout: 15_000
  },
  webServer: process.env.IRONDEV_TAURI_SHELL_BASE_URL
    ? undefined
    : {
        command: webServerCommand,
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 60_000
      },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
});
