# TravelOS Browser End-to-End Test Plan

## Purpose and test boundary

Stage 45 adds Chromium browser journeys for the critical traveller workflows already implemented in TravelOS. The browser uses the real React + Vite application, ASP.NET Core Platform API, JWT authentication and a disposable PostgreSQL 16 database.

Only the AI recommendation-generation upstream response is replaced by a deterministic local Node.js fixture. The browser still calls the real Platform API for recommendation generation, and all trip, itinerary, finance, receipt, authorization, scheduling and persistence operations remain connected to PostgreSQL through the real API. The browser job sets `RateLimits__AuthPerMinute=30` to accommodate the combined multi-account test suite; the application default remains 10 authentication requests per minute when this setting is absent.

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


## Stage 46 — finance and accessibility scenarios

Stage 46 extends the Stage 45 Chromium suite without altering the existing journeys. The two new specs are `finance-accessibility.spec.ts` and the shared `helpers.ts`.

### Additional scenarios and expected results

| ID | Scenario | Expected result |
|---|---|---|
| FIN-01 | Register an owner and a second traveller; accept a real trip invitation | The real Platform API records two distinct accounts and member roles |
| FIN-02 | Create a shared LKR 1,000 meal expense split equally between both members | The saved expense shows two LKR 500 shares and persists in PostgreSQL |
| FIN-03 | Review settlement suggestions | The member who owes money is the debtor; the payer is the creditor; the suggested transfer is LKR 500 |
| FIN-04 | Submit the repayment as the debtor | A pending repayment appears and the debt balance remains unchanged |
| FIN-05 | Attempt to confirm as the debtor | The real Platform API returns HTTP 403; the debtor cannot self-confirm |
| FIN-06 | Sign in as the recipient and confirm the pending repayment | The repayment becomes confirmed and the resulting settlement contains no outstanding balances |
| RCPT-01 | Upload a text file as a receipt | The receipt endpoint rejects non-image content and no receipt is created |
| RCPT-02 | Upload a small PNG and associate it with the saved expense | The API stores the image in the isolated private test directory and displays a manual-review receipt when OCR is not configured |
| RCPT-03 | Enter merchant, date, amount and currency, link the expense and confirm | The user-reviewed details are saved as confirmed; no OCR output is treated as verified automatically |
| RCPT-04 | Retrieve the original receipt with the authenticated file endpoint | HTTP 200, image/png content type and exact uploaded fixture bytes are returned |
| RCPT-05 | Upload the same PNG again | The second receipt is flagged as a possible duplicate; both records persist after browser refresh |
| A11Y-01 | Run axe-core on the unauthenticated registration screen | The test fails on critical or serious WCAG 2.1 A/AA violations |

### Accessibility scope

The automated axe scan uses the WCAG 2.0 A/AA and WCAG 2.1 A/AA rule tags and fails the test on any critical or serious impact violation. It is a targeted automated accessibility check, not a full accessibility conformance statement. It does not replace keyboard-only testing, screen-reader verification, manual focus review, zoom/reflow testing or human evaluation.

### Finance test data and safety

- Test accounts use unique generated `example.test` email addresses and a fixed test-only password.
- Shared expense and repayment calls use the real authenticated Platform API and disposable PostgreSQL 16 database.
- Receipt upload tests use a tiny synthetic PNG buffer created in the test code and an invalid text payload. They do not use personal receipts.
- The API's private receipt storage path is configured under the GitHub runner's temporary directory. The workflow uploads test reports/logs, not the receipt files.
- The AI mock server does not implement receipt OCR. The real receipt endpoint is expected to preserve the upload and report manual review when the OCR service is unavailable.

### Updated local commands

From `apps/web`, run:

```bash
npm install
npx playwright install chromium
npm run typecheck
npm run typecheck:e2e
npm run build
npm run test:e2e
```

The disposable browser API is configured with an authentication request allowance of 30/minute to account for the 2-account registration/login journey in the suite. The normal application default remains 10/minute when no configuration override is supplied. The `test:e2e` script now runs strict TypeScript checking for the Playwright test files and configuration before starting the browser suite. Start the same disposable PostgreSQL database, deterministic AI fixture and test-configured Platform API documented in the Stage 45 setup section.

### Stage 46 limitations

- Chromium remains the only automated browser project.
- axe checks the registration screen only; other workspaces and dynamic states require future scans and manual review.
- The receipt browser flow tests the manual-review path, not a live OCR provider or extraction accuracy.
- The browser test verifies local/private receipt access with an authenticated member token. It does not constitute a production storage security audit.
- These tests do not verify actual bank transfer execution; repayments are application records that require recipient confirmation.


### Stage 46 validation record

Latest verified commit at the time of this record: `7048017914918de73307fb58d68c48568077d8a0`.

GitHub Actions run [#44500311672](https://github.com/harini281/travel_os/actions/runs/44500311672) completed with **7/7 jobs successful**:
- Web typecheck and production build.
- Platform API Release build.
- Platform API unit tests (10 passed; 14 database-dependent tests skipped in this job and covered by the PostgreSQL integration job).
- Platform API PostgreSQL integration tests (24 passed).
- AI API tests (18 passed; 1 warning).
- PostgreSQL 16 migration validation.
- Chromium browser suite (**7 passed**, combining four Stage 45 tests and three Stage 46 tests).

The browser job uploaded `travelos-playwright-e2e-results` (artifact ID `14517361919`), containing the HTML report and test diagnostics. The receipt files themselves remain in the runner's temporary private directory and are not part of the artifact.


## Stage 47 — desktop cross-browser, mobile, keyboard and accessibility matrix

### Browser projects

| Project | Browser/device | Coverage |
|---|---|---|
| chromium | Desktop Chromium | All Stage 45–46 journeys plus desktop cross-browser smoke |
| firefox | Desktop Firefox | Registration, trip creation, timed itinerary activity, refresh persistence and overflow |
| webkit | Desktop WebKit | Same cross-browser smoke journey |
| mobile-chromium | Pixel 7 emulation | Registration, dashboard, trip and itinerary at phone width |
| mobile-webkit | iPhone 14 emulation | Same responsive journey on mobile WebKit |
| tablet-chromium | 768 × 1024 touch viewport | Responsive journey at tablet width |

### Stage 47 test cases

| ID | Scenario | Expected result |
|---|---|---|
| XBR-01 | Run the desktop itinerary smoke journey in Chromium, Firefox and WebKit | Registration, trip creation, timed activity save and refresh persistence all work; no uncaught page errors |
| XBR-02 | Check horizontal layout on desktop browser engines | Document scroll width does not exceed viewport width |
| MOB-01 | Open registration at Pixel 7, iPhone 14 and tablet viewports | Registration form is visible and page has no horizontal overflow |
| MOB-02 | Register and open the dashboard on each responsive project | Dashboard is usable, trip planning entry is visible and there is no horizontal overflow |
| MOB-03 | Create and open a trip itinerary on each responsive project | Day and activity render within viewport bounds without document-level horizontal scrolling |
| A11Y-01 | Scan registration state with axe-core on responsive projects | No critical or serious WCAG 2.1 A/AA violations |
| A11Y-02 | Scan authenticated dashboard state | No critical or serious WCAG 2.1 A/AA violations |
| A11Y-03 | Scan itinerary state with a saved activity | No critical or serious WCAG 2.1 A/AA violations |
| KEY-01 | Tab from the plan-trip control to the next control | Focus advances and receives a visible focus outline or shadow |
| KEY-02 | Shift+Tab back and activate with Enter | Focus returns to the plan-trip button and opens the trip-creation form |

The axe integration checks automated WCAG 2.0/2.1 A/AA rules and fails on critical or serious impact. It is a useful automated gate, not a conformance certificate. Manual screen-reader, full keyboard, zoom/reflow and assistive-technology testing are still necessary.

### Local commands

From `apps/web`:

```bash
npm install
npx playwright install chromium firefox webkit
npm run typecheck
npm run typecheck:e2e
npm run build
npm run test:e2e
```

Desktop engines and responsive devices can be run separately:

```bash
npm run test:e2e:desktop
npm run test:e2e:responsive
```

All projects run against the same real Platform API and disposable PostgreSQL test database. The AI recommendation upstream remains deterministic as in Stage 45; finance, receipt, authentication, itinerary and scheduling endpoints remain real. CI installs all three Playwright browser engines with Linux dependencies.

### Known limitations

- Playwright Firefox and WebKit are automated builds, not every released Firefox/Safari version or a physical device.
- Pixel/iPhone projects are device emulation; physical Android and iOS hardware is not covered.
- axe checks registration, dashboard and itinerary states only; other dynamic panels need additional scans.
- The keyboard test checks representative focus and activation behaviour; it is not a full keyboard-only journey.
- Human screen-reader and accessibility review remains outstanding.


### Stage 47 verified CI run

[GitHub Actions run #44510651024](https://github.com/harini281/travel_os/actions/runs/44510651024) completed successfully on code commit `4fe728cb80625e0ced82c959520f0c708451b51d`.

All seven CI jobs passed:
- Web typecheck and Vite production build.
- Platform API Release build.
- Platform API unit tests: 10 passed; 14 PostgreSQL-dependent tests skipped in the database-free unit job.
- PostgreSQL-backed Platform API integration tests: 24 passed.
- AI API tests: 18 passed, 1 warning.
- PostgreSQL 16 migration validation.
- Cross-browser, mobile and accessibility Playwright matrix: **16 passed in 2.7 minutes**.

The browser job uploaded artifact `travelos-playwright-e2e-results` (ID `14523596375`) with HTML output and failure diagnostics. No live paid AI provider is used; only the deterministic recommendation-generation upstream is mocked. Authentication, trips, itineraries, scheduling, finance and receipts remain backed by the real Platform API and disposable PostgreSQL database.

