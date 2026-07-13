# CLAUDE.md

Project context for Claude Code. Read this before doing any work in this repo.

## Purpose

A portfolio / interview project: a small dashboard application built to
demonstrate end-to-end backend-leaning skills (auth, API design, data layer,
CI/CD, automated testing). Most of the meaningful logic lives in the API.
The frontend is a deliberately thin client.

The secondary goal is **maximising automated test coverage** so the app can be
verified without manual clicking. Treat "is this tested?" as part of "is this
done?". See the Testing section — it is the priority of this project.

## Architecture

Three tiers that deploy independently but change together:

```
interview-site/
├── frontend/            # Vite + React + TypeScript SPA -> Azure Static Web Apps
├── api/                 # .NET Core Web API (business logic lives here)
├── database/            # SDK-style Microsoft.Build.Sql project -> Azure SQL
├── .github/workflows/   # 3 path-filtered workflows, one per tier
└── README.md            # human-facing architecture overview
```

Request flow: the SPA sends the browser to the API's `/auth/login/okta`; the
API runs the OIDC authorization-code flow against Okta server-side, mints its
own JWT, and hands it to the SPA via a one-time code. The SPA then calls the
API with an `Authorization: Bearer <token>` header. The API validates its own
JWT and serves data from Azure SQL. The frontend never talks to Okta or the
database directly.

## Tech stack

- **Frontend:** Vite, React, TypeScript. Routing via react-router. **No auth
  SDK** — login is a full-page redirect to the API, which owns the OIDC flow.
- **API:** .NET Core Web API (C#), **minimal APIs** (see API design section).
  OIDC confidential client to Okta; JWT bearer auth validating **API-minted**
  HS256 tokens.
- **Database:** SDK-style `Microsoft.Build.Sql` (.sqlproj), targeting Azure SQL.
  Schema is source-controlled and built into a DACPAC.

## Auth model

**Server-side OIDC.** The API is the confidential client; the frontend has no
Okta SDK and never sees Okta tokens.

- Login: `GET /auth/login/{provider}` (full-page redirect) → Okta hosted login
  → `GET /auth/callback/{provider}` on the API. The API exchanges the code with
  Okta server-side (authorization code + PKCE, `state` validated via a
  single-use store), calls `/v1/userinfo`, and upserts the user into
  `dbo.[User]`.
- Handoff: the callback redirects to the SPA's `/auth/callback` with a
  60-second single-use code in the URL **fragment**; the SPA exchanges it at
  `POST /auth/token` for tokens. Access tokens never appear in URLs.
- Tokens: the API mints its **own** HS256 JWT (15 min, `Jwt:*` config) plus an
  opaque refresh token (30 days, rotated on every `POST /auth/refresh`; only
  SHA-256 hashes stored in `dbo.RefreshToken`; replaying a revoked token
  revokes the user's whole token family). `POST /auth/logout` revokes.
- Storage in the SPA: access JWT in memory only; refresh token in
  `sessionStorage`. `authFetch` (`frontend/src/api/http.ts`) attaches the
  bearer header and does a single-flight refresh + one retry on 401.
- Extensibility: identity providers implement `IExternalAuthProvider`
  (`api/Services/Auth/`); adding Google/Microsoft is one class + one DI
  registration in Program.cs. Routes are provider-keyed (`/auth/login/google`).
- E2E test provider: `TestAuthProvider` ("test") bounces straight back to the
  API callback with no external IdP, so Playwright can run the full login flow
  without Okta credentials. Registered ONLY when environment is Development
  AND `Auth:EnableTestProvider=true` — it must never exist in production
  (gating covered by `api.tests/Integration/TestProviderGatingTests.cs`).
- Protected surface: `GET /auth/me`, `GET /page-visits/summary` (via
  `.RequireAuthorization()`). Health and the page-visit record/count endpoints
  stay public — the landing-page badge needs them.
- One-time codes (OIDC state + SPA handoff) live in `IMemoryCache` — fine
  single-instance; move to a shared store before scaling out.
- No secrets in the frontend. No connection strings reach the client — all data
  access goes through the authenticated API.
- Local secrets via .NET User Secrets (`Jwt:SigningKey`,
  `Auth:Okta:ClientSecret`); deployed secrets via Azure Key Vault. Non-secret
  auth config (`Jwt:Issuer/Audience`, `Auth:Okta:Issuer/ClientId`,
  `Auth:FrontendBaseUrl`, `Auth:ApiBaseUrl`) lives in appsettings / App Service
  configuration.

## API design

The API uses **minimal APIs, NOT controllers**, organised for structure rather
than dumped inline. Rationale: it's Microsoft's recommended default for new
projects, has a leaner startup, and this project is small enough that
controllers' scale benefits don't apply. The layered architecture (services hold
the business logic) sits *behind* the HTTP edge and is independent of this
choice — do not confuse "layered backend" with "needs controllers".

Structure rules:
- **One endpoint extension class per resource** in `Endpoints/`, exposing a
  `MapXxxEndpoints(this IEndpointRouteBuilder)` method. Do NOT declare endpoints
  inline in `Program.cs`.
- **Route groups** (`MapGroup`) per resource: declare the shared prefix once and
  apply shared concerns (`.RequireAuthorization()`, `.WithTags(...)`, endpoint
  filters) to the whole group rather than per endpoint.
- **Named static handler methods**, not inline lambdas — readable and debuggable,
  and the `Map...` block reads as a table of contents for the resource.
- **Thin handlers:** bind the request, call a service, map the result to a status
  code. All business logic lives in the service layer (`Services/`), which is
  where unit tests target. The endpoint is only the HTTP edge.
- **Inject dependencies by handler parameter** (same DI as a controller ctor).
- **Return concrete typed results, not `IResult`.** Use `TypedResults` (not
  `Results`) and declare the concrete return type on the handler so response
  metadata is inferred for OpenAPI automatically: `Ok` for a bodyless 200,
  `Ok<T>` when there's a body, and `Results<Ok<T>, NotFound>` (etc.) for handlers
  with multiple outcomes. Returning the `IResult` interface hides the status
  codes and shapes from OpenAPI and forces manual `.Produces<T>(...)` annotations
  — avoid it. (Concrete result types live in `Microsoft.AspNetCore.Http.HttpResults`.)
- **`Program.cs` is a thin composition root:** register services + auth, then one
  `app.MapXxxEndpoints()` line per resource — no endpoint logic.

Layout:
```
api/
├── Program.cs            # composition root: DI, auth, one Map line per resource
├── Endpoints/            # one MapXxxEndpoints class per resource
├── Services/             # business logic + interfaces (unit-test target)
│   ├── Auth/             # OIDC providers, JWT minting, refresh tokens
│   ├── Integrations/     # IIntegrationConnector + per-integration connectors
│   └── IntegrationCalls/ # call auditing: recorder, redactor, recording handler
└── Contracts/            # request/response DTOs
```

Walking-skeleton starting point: a single **unauthenticated** `GET /health`
returning 200 in `Endpoints/HealthEndpoints.cs` — enough to deploy and prove the
pipeline before auth or real resources exist.

## Features

### Authentication & dashboard
The landing page (`/`) is public. `/dashboard` is protected
(`frontend/src/auth/ProtectedRoute.tsx`) and shows the signed-in user's profile
(`GET /auth/me`) plus per-page visit totals (`GET /page-visits/summary`), with
a sign-out button. Login is a redirect to the API (see Auth model); the SPA
side lives in `frontend/src/auth/` (AuthContext bootstraps from the stored
refresh token on load) and `frontend/src/pages/AuthCallbackPage.tsx` (handoff
code exchange). `frontend/staticwebapp.config.json` provides the SPA fallback
so these routes survive refresh/deep-link on Static Web Apps.

### Page view tracking
Every page records a visit on mount and displays the running count for that
page in a fixed badge at the bottom-right corner. This must be present on
every page — it is not optional.

Implementation:
- `POST /page-visits` — records the visit (called on mount, fire-and-forget
  with await before count fetch so the count includes the current visit).
- `GET /page-visits/count?pagePath=<path>` — returns the count for that path.
- `frontend/src/api/pageVisits.ts` — thin fetch wrappers for both calls.
- `frontend/src/hooks/usePageVisits.ts` — calls POST then GET sequentially;
  exposes `{ count: number | null }` (null while loading).
- `frontend/src/components/PageViewBadge.tsx` — renders nothing while loading,
  then shows `"{n} views"` in a fixed bottom-right pill.
- `frontend/src/components/Layout.tsx` renders the badge once for all routed
  pages using the router's `location.pathname`; `/auth/callback` sits outside
  the Layout deliberately (transient page, no visit recorded).
- In production, the API base URL is set via the `VITE_API_BASE_URL`
  environment variable (configured in Azure Static Web Apps → Configuration →
  Application settings). Locally, set it in `frontend/.env.local`.
  Tests hardcode `http://localhost:3000` via `vitest/config` `test.env`.

### Integrations pages
The dashboard's Integrations section lists integrations **from the API**
(`GET /api/integrations`, backed by `dbo.Integration` — nothing hardcoded in
the frontend) with a Details button per integration navigating to
`/integrations/{name}` (protected, inside Layout).
`frontend/src/pages/IntegrationDetailPage.tsx` is one shared page for all
integrations: endpoint-check buttons that call the integration's endpoints
and display the returned HTTP status, plus a lifetime per-endpoint statistics
table (calls, success rate, avg/max duration, last call time + status) from
`GET /api/integrations/{name}/statistics`. Each completed check re-fetches
the statistics so the numbers update immediately. API wrappers live in
`frontend/src/api/integrations.ts`.

## Integrations & call auditing

External systems are surfaced through read-through connectors; every
integration call is audited to the database in both directions.

### Connector pattern (`api/Services/Integrations/`)
- `IIntegrationConnector` is the seam and carries only `Name`. Data shapes are
  integration-specific, so the endpoint layer depends on the concrete
  connector; the interface exists for cross-integration enumeration.
- **Salesforce** (`api/Services/Integrations/Salesforce/`) is the first
  implementation: OAuth 2.0 JWT Bearer flow against a Developer Edition org
  (token endpoint and JWT `aud` are `https://login.salesforce.com`, NOT
  test.salesforce.com). `SalesforceTokenProvider` signs an RS256 assertion and
  exchanges it for a session; the response's `instance_url` is the base for
  all data calls — never hardcode it. Sessions are cached in `IMemoryCache`
  (`Salesforce:TokenCacheMinutes`, default 30 — the JWT-bearer response has no
  `expires_in`); a 401 on a data call invalidates the cache and retries once.
- Secrets: `Salesforce:ClientId` / `Salesforce:Username` /
  `Salesforce:PrivateKey` (PEM string) via User Secrets locally and Key Vault
  in Azure — never appsettings, never the repo. Non-secret
  `LoginUrl`/`ApiVersion`/`TokenCacheMinutes` live in appsettings.json.
  Missing secrets → integration endpoints return 502 ProblemDetails; the app
  still boots.
- Integration endpoints live in a route group `/api/integrations/{name}` with
  `.RequireAuthorization()` and the recording filter (below). Failures surface
  as ProblemDetails — 502 for upstream/auth errors, 504 for timeouts — via a
  single categorised `SalesforceApiException`; never an unhandled 500. Org
  gotcha: External Client Apps require the `refresh_token, offline_access`
  OAuth scope for the JWT bearer flow even though it issues no refresh token.

### Call auditing (`dbo.IntegrationCall`)
- Every call is recorded in both directions; `CorrelationId`
  (= `Activity.Current.TraceId`) links an inbound request to the outbound
  calls it triggered.
- Two seams, both feeding one recorder:
  `IntegrationCallRecordingHandler` (a `DelegatingHandler` chained onto each
  integration's typed HttpClients — outbound) and
  `IntegrationCallRecordingFilter` (an endpoint filter on the integration's
  route group — inbound; it runs after authorization, so anonymous 401s are
  never recorded, by design).
- Endpoint identity: inbound = last path segment; outbound = the call site
  tags its `HttpRequestMessage` via `IntegrationCallOptions.EndpointName`.
  Names must match seeded `dbo.IntegrationEndpoint` rows; the INSERT resolves
  the FK and unmatched names save with a NULL link — **the audit write must
  never fail or block the audited call**. `IntegrationName` stays denormalised
  on the row for the same reason.
- All saves go through `IntegrationCallRecorder`: it redacts
  (`IntegrationCallRedactor` — credential form fields, token JSON properties,
  bearer values, PEM blocks, and a JWT-shaped catch-all) and then saves,
  swallowing + logging any failure. **Headers are never stored** (method, URL,
  bodies only), so the Authorization header cannot leak through a redaction
  gap.
- `dbo.Integration` / `dbo.IntegrationEndpoint` are seeded by the DACPAC
  post-deployment script (`database/Scripts/Script.PostDeployment.sql`,
  declared via `<PostDeploy>` in the sqlproj). It is idempotent and runs on
  **every** publish — prod, the e2e workflow, and the Testcontainers deploy in
  the data tests — so seed data is automatically present everywhere,
  including CI data tests.
- The `/api/integrations` meta surface (list + statistics,
  `api/Endpoints/IntegrationsEndpoints.cs`) is deliberately **not** audited —
  reading statistics must not pollute the statistics.

### Adding a new integration (checklist)
1. **Database first:** add `Integration` + `IntegrationEndpoint` seed rows to
   the post-deployment script; deploy.
2. **API:** options class + secrets; connector (and token provider if needed)
   as typed HttpClients, each chained with a recording handler named for the
   integration; tag outbound requests with their endpoint name; route group
   `/api/integrations/{name}` with `.RequireAuthorization()` and a recording
   filter; register in Program.cs; forward the connector to
   `IIntegrationConnector`.
3. **Frontend:** the dashboard list and statistics table pick the new
   integration up automatically from the API. The detail page's check buttons
   are currently wired to auth/accounts-style endpoints — generalise them when
   a second integration's endpoints differ.

## Deployment / CI-CD

- **Frontend** deploys to Azure Static Web Apps (Free tier). Build config:
  `app_location: "frontend"`, `output_location: "dist"`, **API location left
  blank** (the API is a separate App Service, NOT a SWA managed function).
  Workflow: `.github/workflows/azure-static-web-apps-victorious-forest-0f65e0003.yml`.
- **API** deploys to Azure App Service (Basic B1, "Always On" enabled to avoid
  cold starts) via OIDC workload identity — no publish profile, no basic auth.
  Workflow: `.github/workflows/api-deploy.yml`.
- **Database** deploys by publishing the DACPAC to Azure SQL via `azure/sql-action@v2.3`
  using the same OIDC identity as the API. Runs on `windows-latest` (SqlPackage
  is not available on ubuntu runners). Connection string stored in
  `AZURE_SQL_CONNECTION_STRING` repo secret.
  Workflow: `.github/workflows/database-deploy.yml`.
- Each tier has its own workflow under `.github/workflows`, **path-filtered** so
  a change in one tier does not trigger the others. e.g. the frontend workflow
  filters on `frontend/**`, the API on `api/**`, the database on `database/**`.
- A `frontend/staticwebapp.config.json` provides SPA navigation fallback to
  `/index.html` so client-side routes don't 404 on refresh/deep-link.

## Deployment ordering

For coordinated releases that **add** functionality, deploy in dependency order:
**database → API → frontend**. Each tier is deployed only once the tier beneath
it can support it, and each step stays backward-compatible during the
transition, so no tier is ever left depending on something not yet there:

1. **Database first** — new columns/tables/procs exist before anything uses
   them. The existing API keeps working because the change is additive.
2. **API second** — new endpoints/logic exist before the frontend calls them.
   The existing frontend keeps working because the change is additive.
3. **Frontend last** — everything it now depends on is already live.

This is the "expand" half of expand/contract (parallel change). It only holds
while changes are **additive / backward-compatible**.

**Breaking changes** (renaming/dropping a column, removing an endpoint) can't be
done safely in one pass. Use expand/contract across *separate* releases:
1. Expand: add the new thing alongside the old (DB → API → FE as above).
2. Migrate every consumer to the new thing and deploy.
3. Contract: remove the old thing — this **reverses** the order. Destructive
   database cleanup (e.g. dropping the now-unused column) goes **last**, only
   after no API or frontend code references it.

So: *database first when adding, database last when removing.*

**Caveat for this repo:** the path-filtered workflows deploy each tier
independently and do NOT enforce this ordering — a single PR touching all three
tiers may fire three workflows in parallel with no guaranteed sequence. For a
coordinated feature, either (a) split it into sequential PRs/merges in
DB → API → FE order, or (b) use a single orchestrated workflow with job
dependencies (`needs:`) to enforce the sequence. Approach (a) is the standard
here — the stacked per-tier PRs described in Branching & pull requests merge
in exactly this order, so the ordering falls out of the normal process. Note
if a release deviates from it.

## Testing (priority)

Goal: catch regressions automatically, minimise manual testing. Follow a test
pyramid — many fast unit tests, fewer integration tests, a small number of
end-to-end tests. Every new feature ships with tests. Tests run in CI on every
PR and must pass before merge.

### Frontend (`frontend/`)
- **Vitest** as the test runner (Vite-native, fast, Jest-compatible API).
- **React Testing Library** for component tests — assert on behaviour and what
  the user sees, not implementation details.
- **MSW (Mock Service Worker)** to mock API responses in component/integration
  tests, so the UI can be tested without a live backend.
- **Playwright** (`frontend/e2e/`, `npm run test:e2e`) for end-to-end tests of
  the critical paths: public landing + badge, protected-route redirect, and
  the full login journey (sign-in → dashboard → reload survives → logout)
  using the API's gated test provider instead of real Okta. The
  `playwright.config.ts` webServer block boots both the API and the Vite dev
  server; the API needs a reachable SQL Server (CI provides a service
  container via `.github/workflows/e2e.yml`; locally set `E2E_SQL_CONNECTION`).
- Coverage via Vitest's built-in (v8) reporter.

### API (`api/`)
- **xUnit** as the test framework.
- **FluentAssertions** for readable assertions; **NSubstitute** (or Moq) for
  mocking dependencies in unit tests.
- **Unit tests** for business logic in isolation — no I/O, fast.
- **Integration tests** using `WebApplicationFactory<T>` to exercise the API
  in-memory through the real request pipeline (routing, model binding, auth
  middleware). Because the API validates its own HS256 JWTs from a config key,
  tests mint **real tokens with a test signing key**
  (`api.tests/Integration/TestAuth.cs`) and go through the actual JwtBearer
  middleware — stronger than a stub authentication handler.
- **Testcontainers for .NET** to spin up a real SQL Server container for
  data-layer integration tests — deploy the DACPAC into the container, run the
  tests against a real database, tear it down. This tests the actual schema and
  queries, not an in-memory substitute.
- Coverage via **coverlet**.

### Database (`database/`)
- The `Microsoft.Build.Sql` build itself is the first line of defence — a build
  failure catches broken references, missing columns, and invalid objects.
- **tSQLt** for in-database unit tests of programmable objects (stored
  procedures, functions, views, constraints), with each test in its own
  transaction that rolls back.
- Schema/behaviour can also be verified from the API side via the Testcontainers
  flow above (deploy DACPAC -> assert).

### CI
- `pr-ci.yml` runs on every PR to main: a paths-filter job detects which tiers
  changed, then per-tier jobs run only for changed tiers (DACPAC build; dotnet
  test including the Testcontainers data tests — database changes also trigger
  the API suite since the data tests deploy the DACPAC; Vitest + production
  build). The e2e suite is reused via `workflow_call` from `e2e.yml` when any
  tier changed. All jobs feed one always-reporting **"CI gate"** job — the only
  required status check, so path-filtered jobs can skip without wedging the PR.
  Docs-only PRs pass the gate with every tier job skipped.
- A PR cannot merge with failing tests: "CI gate" is required by the
  main-protection ruleset, which also requires PRs for all changes to main
  (no direct pushes, admin bypass via PR only) and blocks force-pushes and
  branch deletion.
- The per-tier deploy workflows still run their tests on push to main after
  merge, path-filtered as with deployment.

## Conventions & commands

Frontend (run from `frontend/`):
- `npm run dev` — local dev server
- `npm run build` — production build to `dist/`
- `npm run test` — Vitest
- `npm run test:e2e` — Playwright

API (run from `api/`):
- `dotnet build`
- `dotnet test`
- `dotnet run`

Database (run from `database/`):
- `dotnet build` — builds the DACPAC

## Branching & pull requests

All changes go through branches and pull requests — **never commit directly to
`main`**, however small the change (docs tweaks included). `main` only advances
by PRs the user has reviewed and merged; Claude opens PRs but does not merge
them.

- **Branch naming:** `feature/<name>-<tier>` with tier suffixes `db` / `api` /
  `frontend`, e.g. `feature/call-auditing-db`, `feature/call-auditing-api`,
  `feature/call-auditing-frontend`. Single-tier changes may drop the suffix
  (`feature/<name>`).
- **Multi-tier features use stacked branches:** `feature/<name>-db` branches
  off `main`, `feature/<name>-api` off the db branch, and
  `feature/<name>-frontend` off the api branch. Each PR then shows only its
  own tier's diff, and each tier builds and tests against the tier beneath it.
- **PRs open together, after testing.** Implement and test all tiers first
  (each tier's build + tests green), then open all the PRs at once. Each PR
  description states the merge order (DB → API → FE) and links the other PRs
  in the stack. After a lower PR merges, retarget the next PR onto `main`
  (GitHub does this automatically when the merged base branch is deleted).
- **Merge order doubles as deployment order.** Merging DB → API → FE fires the
  path-filtered deploy workflows one tier at a time in dependency order — this
  is option (a) from the Deployment ordering caveat and is the standard
  process for additive changes. For contract/removal releases, reverse the
  stack (FE → API → DB) per that section.

## Working agreement for Claude Code

- **All work happens on branches** per the Branching & pull requests section —
  finished, tested work is handed over as PRs for the user to review and
  approve, never committed to `main`.
- **Work one tier at a time.** Scaffold, verify it builds, commit to that
  tier's branch, then move on.
  Do not wire up auth, endpoints, and components in the same pass as scaffolding.
- **Use real tooling to scaffold** (`npm create vite`, `dotnet new`) rather than
  hand-writing project files, so structure is idiomatic.
- **Verify before moving on:** after each tier, run its build (and tests once
  they exist) to confirm a green baseline.
- **Tests are not optional.** When adding a feature, add its tests in the same
  change. If asked to add logic without tests, flag it.
- Ask before introducing new dependencies or deviating from the stack above.