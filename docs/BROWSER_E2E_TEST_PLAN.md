# TravelOS Browser End-to-End Test Plan

## Purpose and test boundary

Stage 45 adds Chromium browser journeys for the critical traveller workflows already implemented in TravelOS. The browser uses the real React + Vite application, ASP.NET Core Platform API, JWT authentication and a disposable PostgreSQL 16 database.

Only the AI recommendation-generation upstream response is replaced by a deterministic local Node.js fixture. The browser still calls the real Platform API for recommendation generation, and all trip, itinerary, authorization, scheduling and persistence operations remain connected to PostgreSQL through the real API.

The suite is intentionally not connected to a paid AI account, production database, or live travel supplier.

## Environment

| Component | Test configuration |
|---|---|
| Browser | Playwright Test 1.65.0, Chromium only |
| Frontend | React + Vite at `http://127.0.0.1:5173` |
| Platform API | ASP.NET Core 8 at `http://127.0.0.1:8080` |
| Database | Disposable PostgreSQL 16 database, migrations 001–017 |
| AI recommendation upstream | Deterministic Node fixture at `http://127.0.0.1:8011` |
| Authentication | Real registration/login and real test-only JWT signing configuration |

The AI fixture returns two fixed recommendations. A question containing `[e2e:fail]` makes the fixture return HTTP 503 so the real Platform API and React error handling can be exercised without a paid provider.

## Scenarios and expected results

| ID | Scenario | Expected result |
|---|---|---|
| AUTH-01 | Register a traveller from the browser | Account is created and the authenticated dashboard appears |
| AUTH-02 | Sign out and submit an invalid password | Login remains on the public auth screen and a visible error is shown |
| AUTH-03 | Call a protected trip API without a bearer token | Platform API returns HTTP 401 |
| TRIP-01 | Create a trip with destination, dates and LKR currency | Trip card shows the saved name, destination and currency |
| TRIP-02 | Reload the browser after trip creation | Trip is fetched from the API and remains visible |
| ITIN-01 | Create an itinerary and add a day in the trip date range | Day appears in the saved itinerary |
| ITIN-02 | Add a timed activity | Title and start/end times appear in the day |
| ITIN-03 | Edit an activity | Updated title is visible after the API save |
| ITIN-04 | Delete an activity | Removed activity is absent from the itinerary |
| ITIN-05 | Approve the itinerary and reload | Approval persists and editing controls remain hidden |
| AI-01 | Request contextual recommendations | Two deterministic cards show rationale, duration, indicative cost, best-for text and verification note |
| AI-02 | Review recommendations before saving | Suggested activities do not appear in saved day activities automatically |
| AI-03 | Simulate an AI upstream 503 | Platform API error is displayed in the browser; no AI API key is needed |
| SCHED-01 | Request a slot with existing timed activities | Returned slot is in the configured planning window and does not overlap the existing activity |
| SCHED-02 | Inspect initial slot selector | No slot is selected automatically and “Save with this time” is disabled |
| SCHED-03 | Explicitly select a slot and save | Save is enabled only after selection; saved activity displays the selected start/end times |
| SCHED-04 | Refresh after saving with time | Start/end times are reloaded from PostgreSQL via the Platform API |
| SCHED-05 | Use a window fully occupied by an existing activity | “No suitable slot” is shown and no timed save action is offered |
| SCHED-06 | Choose “Add without a time” | Activity is saved without start/end values and remains untimed after refresh |
| SCHED-07 | Request the same recommendations after a save | The already-added recommendation is marked as added and no duplicate save buttons appear |
| AUTHZ-01 | Invite a second account and accept as a trip member | Member sees the shared trip and saved itinerary |
| AUTHZ-02 | Inspect member workspace | Owner-only AI recommendation, day and activity editing controls are not rendered |
| AUTHZ-03 | Member calls schedule and activity mutation endpoints directly | Real Platform API rejects both owner-only requests with HTTP 404 |

The test data uses generated email addresses and a fixed, non-production password that exists only in test source. No personal or production accounts are used.

## Local execution

### 1. Prepare PostgreSQL

Follow the repository's [database setup guide](DATABASE_SETUP.md). Use a dedicated empty test database; do not point the test connection string at a normal development or production database.

Apply migration scripts 001–017 in the same order as the `platform-api-integration-tests` and `browser-e2e` jobs in `.github/workflows/ci.yml`.

### 2. Start the deterministic AI fixture

From the repository root:

```bash
node tests/e2e/mock-ai-server.mjs
```

The fixture listens on port 8011 by default. Override it with `E2E_AI_MOCK_PORT` only when the API's `AiService__BaseUrl` is updated to match.

### 3. Start the Platform API

From the repository root (PowerShell example):

```powershell
$env:ConnectionStrings__TravelOS = "Host=localhost;Port=5432;Database=travelos_e2e;Username=postgres;Password=YOUR_TEST_PASSWORD"
$env:Jwt__Issuer = "TravelOS.Playwright"
$env:Jwt__Audience = "TravelOS.Playwright.Client"
$env:Jwt__SigningKey = "REPLACE_WITH_A_RANDOM_TEST_ONLY_SECRET_AT_LEAST_32_BYTES"
$env:Cors__AllowedOrigins__0 = "http://127.0.0.1:5173"
$env:AiService__BaseUrl = "http://127.0.0.1:8011"
$env:Receipts__StoragePath = "../../data/playwright-receipts"
dotnet run --project services/platform-api/TravelOS.PlatformApi.csproj --configuration Release --urls http://127.0.0.1:8080
```

Keep the signing key private to the local test environment. Do not commit it or reuse it outside testing.

### 4. Install and run Playwright

From `apps/web`:

```bash
npm install
npx playwright install chromium
```

PowerShell:

```powershell
$env:VITE_PLATFORM_API_URL = "http://127.0.0.1:8080"
$env:E2E_API_URL = "http://127.0.0.1:8080"
$env:PLAYWRIGHT_BASE_URL = "http://127.0.0.1:5173"
npm run typecheck
npm run build
npm run test:e2e
```

Bash:

```bash
export VITE_PLATFORM_API_URL=http://127.0.0.1:8080
export E2E_API_URL=http://127.0.0.1:8080
export PLAYWRIGHT_BASE_URL=http://127.0.0.1:5173
npm run typecheck
npm run build
npm run test:e2e
```

The Playwright configuration starts and waits for the Vite dev server. PostgreSQL, the AI fixture and the Platform API must already be running.

Useful commands:

```bash
npm run test:e2e          # headless Chromium suite
npm run test:e2e:ui       # Playwright interactive UI
npm run test:e2e:report   # open the last HTML report
```

## GitHub Actions behaviour

The dedicated `Chromium browser end-to-end tests` job:

1. Starts an isolated PostgreSQL 16 service.
2. Applies database migrations 001–017 with `ON_ERROR_STOP=1`.
3. Installs the web dependencies, runs `npm run typecheck` and `npm run build`.
4. Installs Chromium and required Ubuntu dependencies.
5. Starts the deterministic AI fixture on port 8011.
6. Starts the real Platform API against the disposable database with test-only JWT settings.
7. Runs `npm run test:e2e`; Playwright starts Vite and waits for port 5173.
8. Uploads the HTML report, screenshots, traces and service logs as a workflow artifact, even when the tests fail.

The existing web, AI API, Platform API build, Platform API unit-test, PostgreSQL API integration-test and migration-validation jobs remain in place.

## Failure handling and diagnostics

- A failed assertion fails the browser job; required tests are not skipped to obtain a green build.
- CI uses one retry to tolerate transient browser startup timing, but the retry is visible in the report.
- Screenshots are captured on failure.
- Traces and videos are retained on failure and can be opened from the Playwright HTML report.
- API and AI fixture logs are attached to the workflow artifact.
- A failing API request is surfaced by the app's existing error alert; the AI fixture has a controlled 503 path for this assertion.
- When API health does not become ready, the workflow prints the Platform API log and fails before browser tests start.

## Known limitations and remaining manual verification

- Chromium is the only automated browser project in this stage. Firefox, WebKit, mobile device layouts and assistive-technology checks remain separate work.
- The deterministic AI fixture validates the response contract and browser flow, not real model quality, provider cost, rate limits or live-provider latency.
- External maps, routing, opening-hours, weather, currency and flight providers are not mocked as part of these journeys; they are outside the tested recommendation flow.
- The tests do not validate production HTTPS, deployment secrets, production database operations, email delivery, password recovery, MFA, payment or booking integrations.
- Manual testing is still required for screen sizes, keyboard-only navigation, accessibility, real-provider AI verification, deployment configuration and operational recovery.
- The browser suite verifies the explicitly listed flows; it is not a claim that every TravelOS screen or integration is fully covered.

## Safety

Use only generated test accounts and a disposable PostgreSQL database. Never run the suite against a production or personal database. Do not store production API keys or JWT secrets in Playwright configuration, fixtures or browser storage.
