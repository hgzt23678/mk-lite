import { defineConfig, devices } from '@playwright/test';

const externalBaseURL = process.env.PLAYWRIGHT_BASE_URL?.replace(/\/$/, '');

export default defineConfig({
  testDir: '.',
  testMatch: /.*\.spec\.ts/,
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'artifacts/report' }]],
  outputDir: 'artifacts/results',
  use: {
    baseURL: externalBaseURL ?? 'http://127.0.0.1:5099',
    locale: 'ja-JP',
    timezoneId: 'UTC',
    screenshot: externalBaseURL === undefined ? 'only-on-failure' : 'off',
    trace: externalBaseURL === undefined ? 'retain-on-failure' : 'off',
    video: 'off'
  },
  webServer: externalBaseURL === undefined ? {
    command: 'bash ../../eng/run-frontend-test-host.sh',
    url: 'http://127.0.0.1:5099/',
    cwd: __dirname,
    reuseExistingServer: false,
    timeout: 120_000,
    stdout: 'pipe',
    stderr: 'pipe'
  } : undefined,
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } }
  ]
});
