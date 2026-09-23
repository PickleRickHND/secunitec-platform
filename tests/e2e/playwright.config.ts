import { defineConfig, devices } from '@playwright/test';

// E2E contra el compose levantado (docs/PLAN.md §8): login PKCE → crear factura → emitir → panel de resiliencia con 429.
// Un solo worker y en orden: los inicios de sesión comparten el límite de /connect/token (5 por minuto por IP).
export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.SPA_URL ?? 'http://localhost:3000',
    // El certificado del gateway es el de desarrollo de .NET (scripts/dev-cert.sh).
    ignoreHTTPSErrors: true,
    locale: 'es-HN',
    timezoneId: 'America/Tegucigalpa',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
