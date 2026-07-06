import { defineConfig } from '@playwright/test'

const API_URL = 'http://localhost:5068'
const APP_URL = 'http://localhost:5173'

// The API needs a real SQL Server (login upserts users / stores refresh
// tokens). CI provides one as a service container; locally, point
// E2E_SQL_CONNECTION at any SQL Server before running npm run test:e2e.
const SQL_CONNECTION =
  process.env.E2E_SQL_CONNECTION ??
  'Server=localhost,1433;Database=IntegrationDashboardE2E;User Id=sa;Password=E2e_Passw0rd!;TrustServerCertificate=True;Encrypt=False'

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['junit', { outputFile: 'test-results/e2e-junit.xml' }],
    ['html', { open: 'never' }],
  ],
  use: {
    baseURL: APP_URL,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --project ../api',
      url: `${API_URL}/health`,
      timeout: 180_000,
      reuseExistingServer: !process.env.CI,
      env: {
        ASPNETCORE_URLS: API_URL,
        ASPNETCORE_ENVIRONMENT: 'Development',
        Auth__EnableTestProvider: 'true',
        Auth__FrontendBaseUrl: APP_URL,
        Auth__ApiBaseUrl: API_URL,
        Jwt__SigningKey: 'e2e-signing-key-not-a-secret-0123456789abcdef',
        Jwt__Issuer: 'IntegrationDashboard.Api',
        Jwt__Audience: 'IntegrationDashboard.Frontend',
        ConnectionStrings__DefaultConnection: SQL_CONNECTION,
      },
    },
    {
      command: 'npm run dev',
      url: APP_URL,
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
      env: {
        VITE_API_BASE_URL: API_URL,
        VITE_AUTH_PROVIDER: 'test',
      },
    },
  ],
})
