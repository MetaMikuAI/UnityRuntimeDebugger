import { defineConfig, devices } from '@playwright/test';

declare const process: { env: Record<string, string | undefined> };

// Runs the web console against the fake game (build it first: dotnet build tests/FakeGame -c Release).
const port = 18797;

export default defineConfig({
  testDir: 'e2e',
  timeout: 30_000,
  fullyParallel: false,
  workers: 1,
  reporter: process.env.CI ? 'github' : 'list',
  use: { baseURL: `http://127.0.0.1:${port}`, trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } }, testIgnore: /mobile/ },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, testMatch: /mobile/ },
  ],
  webServer: {
    command: `dotnet ../tests/FakeGame/bin/Release/net10.0/Assembly-CSharp.dll ${port} e2e`,
    url: `http://127.0.0.1:${port}/help`,
    reuseExistingServer: !process.env.CI,
  },
});
