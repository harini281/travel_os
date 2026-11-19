import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";

export default defineConfig({
  testDir: "./e2e",
  testMatch: "**/*.spec.ts",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  timeout: 90_000,
  expect: {
    timeout: 12_000,
  },
  reporter: [
    ["list"],
    ["html", { outputFolder: "playwright-report", open: "never" }],
  ],
  outputDir: "test-results",
  use: {
    baseURL,
    headless: true,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
    video: "retain-on-failure",
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
    {
      name: "firefox",
      testMatch: "**/cross-browser.spec.ts",
      use: {
        browserName: "firefox",
        viewport: { width: 1365, height: 768 },
      },
    },
    {
      name: "webkit",
      testMatch: "**/cross-browser.spec.ts",
      use: {
        browserName: "webkit",
        viewport: { width: 1365, height: 768 },
      },
    },
    {
      name: "mobile-chromium",
      testMatch: "**/responsive-accessibility.spec.ts",
      use: {
        ...devices["Pixel 7"],
        browserName: "chromium",
      },
    },
    {
      name: "mobile-webkit",
      testMatch: "**/responsive-accessibility.spec.ts",
      use: {
        ...devices["iPhone 14"],
        browserName: "webkit",
      },
    },
    {
      name: "tablet-chromium",
      testMatch: "**/responsive-accessibility.spec.ts",
      use: {
        browserName: "chromium",
        viewport: { width: 768, height: 1024 },
        deviceScaleFactor: 1,
        isMobile: true,
        hasTouch: true,
      },
    },
  ],
  webServer: {
    command: "npm run dev -- --host 127.0.0.1 --port 5173 --strictPort",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    stdout: "pipe",
    stderr: "pipe",
  },
});
