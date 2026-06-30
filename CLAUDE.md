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

Request flow: React SPA authenticates via Okta (OIDC), receives a token, and
calls the API with an `Authorization: Bearer <token>` header. The API validates
the JWT and serves data from Azure SQL. The frontend never talks to the
database directly.

## Tech stack

- **Frontend:** Vite, React, TypeScript. Routing via react-router. Okta via
  `@okta/okta-react` + `@okta/okta-auth-js`.
- **API:** .NET Core Web API (C#), **minimal APIs** (see API design section).
  JWT bearer auth validating Okta-issued tokens.
- **Database:** SDK-style `Microsoft.Build.Sql` (.sqlproj), targeting Azure SQL.
  Schema is source-controlled and built into a DACPAC.

## Auth model

- Okta OIDC. Frontend handles the login redirect and token acquisition.
- API validates the JWT (issuer, audience, signature, expiry) on every request.
- No secrets in the frontend. No connection strings reach the client — all data
  access goes through the authenticated API.
- Local secrets via .NET User Secrets; deployed secrets via Azure Key Vault.

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
- **Use `TypedResults`** (not `Results`) for strongly-typed returns that feed
  OpenAPI accurately and give automatic problem-details responses.
- **`Program.cs` is a thin composition root:** register services + auth, then one
  `app.MapXxxEndpoints()` line per resource — no endpoint logic.

Layout:
```
api/
├── Program.cs            # composition root: DI, auth, one Map line per resource
├── Endpoints/            # one MapXxxEndpoints class per resource
├── Services/             # business logic + interfaces (unit-test target)
└── Contracts/            # request/response DTOs
```

Walking-skeleton starting point: a single **unauthenticated** `GET /health`
returning 200 in `Endpoints/HealthEndpoints.cs` — enough to deploy and prove the
pipeline before auth or real resources exist.

## Deployment / CI-CD

- **Frontend** deploys to Azure Static Web Apps (Free tier). Build config:
  `app_location: "frontend"`, `output_location: "dist"`, **API location left
  blank** (the API is a separate App Service, NOT a SWA managed function).
- **API** deploys to Azure App Service (Basic B1, "Always On" enabled to avoid
  cold starts). *Confirm this resource exists before finalising its workflow;
  leave the workflow as a documented stub until then.*
- **Database** deploys by publishing the DACPAC to Azure SQL.
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
dependencies (`needs:`) to enforce the sequence. Note which approach a release
uses.

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
- **Playwright** for a small set of end-to-end tests covering critical paths
  (login redirect, protected route, dashboard renders data).
- Coverage via Vitest's built-in (v8) reporter.

### API (`api/`)
- **xUnit** as the test framework.
- **FluentAssertions** for readable assertions; **NSubstitute** (or Moq) for
  mocking dependencies in unit tests.
- **Unit tests** for business logic in isolation — no I/O, fast.
- **Integration tests** using `WebApplicationFactory<T>` to exercise the API
  in-memory through the real request pipeline (routing, model binding, auth
  middleware), with auth stubbed via a test authentication handler.
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
- One GitHub Actions job (or step set) per tier runs that tier's tests,
  path-filtered as with deployment.
- A PR cannot merge with failing tests.

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

## Working agreement for Claude Code

- **Work one tier at a time.** Scaffold, verify it builds, commit, then move on.
  Do not wire up auth, endpoints, and components in the same pass as scaffolding.
- **Use real tooling to scaffold** (`npm create vite`, `dotnet new`) rather than
  hand-writing project files, so structure is idiomatic.
- **Verify before moving on:** after each tier, run its build (and tests once
  they exist) to confirm a green baseline.
- **Tests are not optional.** When adding a feature, add its tests in the same
  change. If asked to add logic without tests, flag it.
- Ask before introducing new dependencies or deviating from the stack above.