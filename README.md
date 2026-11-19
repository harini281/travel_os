# TravelOS

**The Intelligent Operating System for Travel**

TravelOS is a multi-service travel-planning platform built with React, ASP.NET Core, PostgreSQL and Python/FastAPI. It brings trip planning, group coordination, budgets, itinerary intelligence and AI-assisted recommendations into one workspace.

> **Current development status:** Stage 51 — security testing and penetration-test preparation is under implementation on `stage51-security-testing`, based on Stage 50. Stages 49 and 50 remain open and unmerged in their own draft pull requests (#51 and #52). This is an active development project, not yet a production-ready booking platform.

## 1. Features implemented

### Accounts, trips and collaboration
- Traveller registration/login, JWT authentication and trip ownership/member access.
- Create, update and manage trips and invitations.
- Shared traveller profiles, interests, travel preferences, polls and voting.
- In-app notifications and scheduled reminders.

### Itinerary planning and intelligence
- Day-by-day itineraries with activities, locations, costs and editable start/end times.
- AI-generated itinerary drafts with review and explicit owner approval before applying.
- Daily schedule conflict detection, planning buffers and suggested time adjustments.
- Route-aware timing estimates, attraction opening-hours checks and interactive Leaflet maps.
- Flight-aware journey timeline and airport/ground-transfer information.
- Weather disruption assessments and a contextual Trip Assistant.
- Stage 39: AI activity ideas for a selected day, focused on weather alternatives, budget, group interests or schedule balance. Suggestions are review-only and do not edit the saved itinerary.

### Budgets and shared expenses
- Trip budget caps, planned categories, shared expenses and participant split methods.
- Expense editing with audit history, balance calculations and suggested settlements.
- Recipient-confirmed repayment tracking; the app does not initiate bank transfers.
- Receipt upload metadata and optional AI receipt extraction.
- Currency reference conversions, multi-currency spending reports and budget alerts.

### Travel support
- Flight records and optional FlightAware status refresh.
- Airport/connection and ground-transfer estimates.
- Open-Meteo weather assessments, OpenStreetMap place data, OSRM route estimates.
- Per-trip timezone display and read-only offline itinerary snapshots.

**Important:** TravelOS does not currently sell tickets, book hotels, guarantee provider availability, or guarantee live route/traffic, opening-hour or price information. Verify important details with the relevant supplier.

## 2. Technology stack

| Layer | Technology |
|---|---|
| Web client | React, TypeScript, Vite, Leaflet |
| Platform API | ASP.NET Core 8, C#, Entity Framework Core |
| Database | PostgreSQL 16, Npgsql |
| AI service | Python 3.11+, FastAPI, Pydantic, OpenAI Responses API (optional) |
| Maps and travel data | OpenStreetMap, OSRM, Open-Meteo, Frankfurter; optional FlightAware |
| CI | GitHub Actions |

## 3. Prerequisites

Install these before starting:

- Git
- Node.js 22+
- .NET 8 SDK
- Python 3.11+
- Docker Desktop / Docker Engine with Docker Compose
- PostgreSQL client tools (`psql`) for applying migrations manually

## 4. Configuration files

There are **three example environment files** to copy. Do not commit real secrets.

| File | What it configures | Required? |
|---|---|---|
| `.env.example` → `.env` (repository root) | Local PostgreSQL container values and reference settings for the API/AI services | Yes for the documented Docker setup |
| `apps/web/.env.example` → `apps/web/.env.local` | Browser's platform API URL | Yes |
| `deploy/production.env.example` → `deploy/production.env` | Production Compose settings; keep the copied file out of Git and protect it with restrictive permissions | Only for the production-style Compose stack |
| `services/platform-api/appsettings.json` | Non-secret .NET logging/default configuration | Already present |
| `services/platform-api/Properties/launchSettings.json` | Local API profile and port 8080 | Already present |
| `services/ai-api/requirements.txt` | Python service dependencies | Already present |
| `docker-compose.yml` | PostgreSQL container, port, volume and initial SQL scripts | Already present |
| `.github/workflows/ci.yml` | Automated build, test and migration checks | Already present |

### Platform API settings

The API process needs these environment variables. The root `.env` file is read by Docker Compose; it is **not automatically loaded** into a separately started `dotnet run` process.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__TravelOS` | PostgreSQL connection string |
| `Jwt__Issuer` | JWT issuer (local value: `TravelOS`) |
| `Jwt__Audience` | JWT audience (local value: `TravelOS.Client`) |
| `Jwt__SigningKey` | Unique secret of at least 32 bytes; mandatory |
| `Cors__AllowedOrigins__0` | Exact web origin, normally `http://localhost:5173` |
| `AiService__BaseUrl` | AI service URL, normally `http://localhost:8001` |
| `AI_SERVICE_TOKEN` | Shared server-only credential (32+ characters) sent by the Platform API to the AI API; never expose it to the browser |
| `Receipts__StoragePath` | Private receipt storage directory |
| `FLIGHTAWARE_API_KEY` | Optional FlightAware AeroAPI key for live flight refresh |

### AI service settings

Set these in the terminal where Uvicorn is started. The Python service does **not** automatically read the repository root `.env` file. The service now rejects unsupported provider mode values and fails startup when a live provider is selected without OPENAI_API_KEY or when its provider base URL is not HTTPS.

| Variable | Purpose |
|---|---|
| `AI_PROVIDER` | `demo` (default) or `openai` |
| `OPENAI_API_KEY` | Required only when enabling OpenAI itinerary generation/recommendations |
| `OPENAI_MODEL` | Optional model override; example: `gpt-4.1-mini` |
| `AI_RECEIPT_PROVIDER` | Receipt extraction provider; keep `demo` unless deliberately configured |
| `AI_SERVICE_TOKEN` | Same server-only token configured in the Platform API process; protects planner and receipt routes. The health endpoint remains unauthenticated for container health checks |

Stage 39 returns no generated suggestions in demo mode. To use live AI recommendations, configure `AI_PROVIDER=openai` and `OPENAI_API_KEY`. Provider usage may incur charges. Never put an AI or FlightAware key in a `VITE_*` frontend variable.

## 5. Run TravelOS locally

Run each service in its own terminal. The commands below use PowerShell on Windows; equivalent Bash commands are included where environment-variable syntax differs.

### Step 1 — Get the code and configure PostgreSQL

```bash
git clone https://github.com/harini281/travel_os.git
cd travel_os
```

PowerShell:

```powershell
Copy-Item .env.example .env
docker compose up -d postgres
docker compose ps
```

Bash:

```bash
cp .env.example .env
docker compose up -d postgres
docker compose ps
```

The container automatically applies the first three bootstrap scripts to a **new empty volume**: `001_initial_schema.sql`, `002_identity_schema.sql` and `003_itinerary_constraints.sql`.

Apply the remaining migrations in order. From the repository root, using PowerShell:

```powershell
$migrations = @(
  "003_trip_invitations.sql",
  "004_trip_member_preferences.sql",
  "005_group_polls.sql",
  "006_shared_expenses.sql",
  "007_trip_repayments.sql",
  "008_trip_expense_audits.sql",
  "009_trip_receipts.sql",
  "010_currency_conversions.sql",
  "011_conversion_source_expense_date.sql",
  "012_trip_budget_alerts.sql",
  "013_trip_weather_assessments.sql",
  "014_trip_flights.sql",
  "015_flight_journey_groups.sql",
  "016_activity_schedule_times.sql",
  "017_trip_notifications.sql"
)
foreach ($migration in $migrations) {
  psql -h localhost -p 5432 -U travelos -d travelos -v ON_ERROR_STOP=1 -f "database/migrations/$migration"
  if ($LASTEXITCODE -ne 0) { throw "Migration failed: $migration" }
}
```

Bash:

```bash
for migration in \
  003_trip_invitations.sql 004_trip_member_preferences.sql 005_group_polls.sql \
  006_shared_expenses.sql 007_trip_repayments.sql 008_trip_expense_audits.sql \
  009_trip_receipts.sql 010_currency_conversions.sql 011_conversion_source_expense_date.sql \
  012_trip_budget_alerts.sql 013_trip_weather_assessments.sql 014_trip_flights.sql \
  015_flight_journey_groups.sql 016_activity_schedule_times.sql 017_trip_notifications.sql
do
  PGPASSWORD=change-me-local-only psql -h localhost -p 5432 -U travelos -d travelos \
    -v ON_ERROR_STOP=1 -f "database/migrations/$migration" || exit 1
done
```

If you have an older database volume, **do not blindly rerun migrations or delete the volume**. Back up your data first and apply only the migrations that have not already been applied. `docker compose down -v` deletes the local database volume.

### Step 2 — Start the Platform API

Open a new terminal. Set a unique random internal service token of at least 32 characters; use exactly the same value in the AI API terminal in Step 3. The value below is an instruction, not a reusable secret:

PowerShell:

```powershell
$env:AI_SERVICE_TOKEN = "REPLACE_WITH_THE_SAME_PRIVATE_RANDOM_TOKEN_IN_BOTH_SERVICE_TERMINALS"
```

Bash:

```bash
export AI_SERVICE_TOKEN="REPLACE_WITH_THE_SAME_PRIVATE_RANDOM_TOKEN_IN_BOTH_SERVICE_TERMINALS"
```


```powershell
cd services/platform-api
$env:ConnectionStrings__TravelOS = "Host=localhost;Port=5432;Database=travelos;Username=travelos;Password=change-me-local-only"
$env:Jwt__Issuer = "TravelOS"
$env:Jwt__Audience = "TravelOS.Client"
$env:Jwt__SigningKey = "REPLACE_WITH_A_UNIQUE_RANDOM_SECRET_AT_LEAST_32_BYTES"
$env:Cors__AllowedOrigins__0 = "http://localhost:5173"
$env:AiService__BaseUrl = "http://localhost:8001"
$env:Receipts__StoragePath = "../../data/private-receipts"
dotnet restore
dotnet run
```

For macOS/Linux, use `export VARIABLE=value` instead of PowerShell's `$env:VARIABLE = "value"`. The signing key above is a placeholder: replace it with a private random secret before running. Do not use the example key in a deployed environment.

### Step 3 — Start the AI API

Open another terminal from the repository root and set the **same** `AI_SERVICE_TOKEN` value that you set for the Platform API in Step 2. Keep it server-side; never create a `VITE_*` variable for this secret.


```powershell
cd services/ai-api
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:AI_PROVIDER = "demo"
uvicorn app.main:app --reload --port 8001
```

To enable real AI generation instead of demo mode, stop/restart Uvicorn after setting:

```powershell
$env:AI_PROVIDER = "openai"
$env:OPENAI_API_KEY = "YOUR_PRIVATE_API_KEY"
$env:OPENAI_MODEL = "gpt-4.1-mini"
uvicorn app.main:app --reload --port 8001
```

macOS/Linux virtual environment activation: `source .venv/bin/activate`. Set the same AI variables with `export`.

### Step 4 — Start the web app

Open another terminal:

```powershell
cd apps/web
Copy-Item .env.example .env.local
npm install
npm run dev
```

The local frontend variable should be:

```env
VITE_PLATFORM_API_URL=http://localhost:8080
```

### Step 5 — Open the services

| Service | Local URL |
|---|---|
| TravelOS web app | http://localhost:5173 |
| Platform API | http://localhost:8080 |
| Platform API health | http://localhost:8080/health |
| AI API | http://localhost:8001 |
| AI API Swagger docs | http://localhost:8001/docs |
| PostgreSQL | localhost:5432 |

Register an account in the web app, sign in, create a trip and open its itinerary workspace. For Stage 39, create an itinerary with at least one day, then use **Improve a day in your itinerary** to request recommendations. In demo mode, the service deliberately explains that AI generation is not enabled.

## 6. Run the tests and builds

Run these from the repository root unless stated otherwise.

### Web typecheck and production build

```bash
cd apps/web
npm install
npm run build
```

### Platform API build

```bash
dotnet restore services/platform-api/TravelOS.PlatformApi.csproj
dotnet build services/platform-api/TravelOS.PlatformApi.csproj --configuration Release
```

### AI API tests

```bash
python -m pip install -r services/ai-api/requirements.txt
python -m pytest -q
```

### Database migration validation

The GitHub Actions workflow starts a clean PostgreSQL 16 service and applies migrations 001–017 in order. To reproduce this locally, use a fresh disposable database and apply the same migration list. Do not use a production or data-bearing database for this check.

### Latest verified CI result

<badge color="success">All 4 CI jobs passed</badge>

GitHub Actions run **#136** for the Stage 39 pull request completed successfully on 19 November 2026:

| Check | Result |
|---|---|
| Web TypeScript check and Vite build | PASS |
| ASP.NET Core Platform API build | PASS |
| AI API automated tests | PASS — 18 passed, 1 warning (Stage 39 run) |
| Platform API unit tests | Added in Stage 42; CI result pending |
| PostgreSQL migrations 001–017 | PASS |

These are the checks currently automated in CI. They do **not** mean every user journey, external provider integration, deployed environment or production security scenario has been end-to-end tested.

## 7. Stage 39 — AI activity recommendations

The itinerary workspace can request up to six contextual activity ideas for a selected itinerary day. The request includes trip destination/date, saved activities, group interests and travel preferences, saved weather assessments and same-currency budget context.

Recommendation focus options:
- All planning factors
- Weather alternatives
- Budget-friendly ideas
- Group interests
- Schedule balance

Each suggestion includes a reason, category, indicative duration/cost, who it may suit and a verification note. The AI provider uses a structured response schema. Requests are authenticated, trip membership is checked and generation is rate limited.

**Review-only behaviour:** suggestions are not saved, booked, or automatically inserted into the itinerary. Check venue details, accessibility, opening hours and prices yourself.

Verification checklist: [docs/AI_ACTIVITY_RECOMMENDATIONS_TEST_PLAN.md](docs/AI_ACTIVITY_RECOMMENDATIONS_TEST_PLAN.md).


### Stage 41 — AI recommendation scheduling

Trip owners can request suitable time slots for a generated activity recommendation. The owner supplies a planning-day window and buffer; TravelOS compares the recommendation duration with saved, timed activities and returns available gaps with explanations. Existing overlaps and activities without times are disclosed.

The owner reviews the proposed slot and explicitly chooses **Save with this time** to add the recommendation with start/end times, or chooses the separate **Add without a time** action. The suggestion endpoint is read-only; it never inserts or changes activities automatically. Approved itineraries remain read-only and the scheduling endpoint is owner-only.

Slots are advisory calculations from saved times and the supplied planning window. They do not verify travel time, traffic, venue opening hours, availability or live conditions. See [Stage 41 verification scenarios](docs/AI_ACTIVITY_RECOMMENDATIONS_TEST_PLAN.md#stage-41--ai-recommendation-scheduling).

### Stage 40 — Traveller-approved AI recommendations

Trip owners can now explicitly add an AI-generated recommendation to the selected itinerary day. The action uses the existing owner-authorized activity creation API, preserves the suggestion's title, rationale, verification note, indicative duration and estimated cost/currency, and reloads the saved itinerary. Duplicate titles on the selected day are blocked in the workspace. Approved itineraries remain read-only.

Newly added suggestions intentionally have no invented place name, coordinates or start/end times. The owner should verify the venue and complete those details before relying on schedule, map or route analysis. This action does not book a venue or approve the itinerary.

See the Stage 40 scenarios in [the AI activity recommendations test plan](docs/AI_ACTIVITY_RECOMMENDATIONS_TEST_PLAN.md).


### Stage 45 — Playwright browser end-to-end testing

Stage 45 adds a dedicated Chromium browser suite alongside the existing API and database test infrastructure. The tests open the real React + Vite application, register and sign in through the real ASP.NET Core authentication endpoints, create trips and itineraries in a disposable PostgreSQL 16 database, and verify browser-visible persistence and permissions after refresh.

Only the AI recommendation-generation upstream response is replaced by a deterministic local Node fixture. The React application still calls the real Platform API for recommendation requests, and all trip creation, itinerary writes, scheduling, JWT authorization and PostgreSQL persistence remain real. The fixture uses no paid AI key and includes a controlled 503 path for browser error-handling assertions.

The recommendation scheduling selector now requires a deliberate choice: no slot is preselected, and **Save with this time** remains disabled until the traveller selects an available slot.

#### Install and run browser tests locally

From the repository root:

```bash
cd apps/web
npm install
npx playwright install chromium
```

Before running the browser suite, start a **disposable PostgreSQL database** and apply migrations 001–017 listed in `.github/workflows/ci.yml`. Start the Platform API with test-only configuration and the deterministic AI fixture:

```powershell
# Terminal 1 — repository root
node tests/e2e/mock-ai-server.mjs

# Terminal 2 — repository root (PowerShell shown)
$env:ConnectionStrings__TravelOS = "Host=localhost;Port=5432;Database=travelos_e2e;Username=postgres;Password=YOUR_TEST_PASSWORD"
$env:Jwt__Issuer = "TravelOS.Playwright"
$env:Jwt__Audience = "TravelOS.Playwright.Client"
$env:Jwt__SigningKey = "REPLACE_WITH_A_PRIVATE_TEST_SECRET_AT_LEAST_32_BYTES"
$env:RateLimits__AuthPerMinute = "30" # Disposable browser test only; production default remains 10/minute.
$env:Cors__AllowedOrigins__0 = "http://127.0.0.1:5173"
$env:AiService__BaseUrl = "http://127.0.0.1:8011"
$env:Receipts__StoragePath = "../../data/playwright-receipts"
dotnet run --project services/platform-api/TravelOS.PlatformApi.csproj --configuration Release --no-launch-profile --urls http://127.0.0.1:8080

# Terminal 3 — apps/web
$env:VITE_PLATFORM_API_URL = "http://127.0.0.1:8080"
$env:E2E_API_URL = "http://127.0.0.1:8080"
$env:PLAYWRIGHT_BASE_URL = "http://127.0.0.1:5173"
npm run typecheck
npm run build
npm run test:e2e
```

Use a separate local test database, never a production or personal database. Replace the signing-key placeholder with a private value before starting the API; do not commit it. On macOS/Linux, use `export NAME=value` for environment variables.

Available Playwright commands (from `apps/web`):

```bash
npm run test:e2e
npm run test:e2e:ui
npm run test:e2e:report
```

#### Browser coverage

- Account registration, successful authenticated access, invalid login feedback and unauthenticated API rejection.
- Trip creation, destination/date/currency display and persistence after browser refresh.
- Itinerary/day/activity creation, editing, deletion, approval and read-only state after refresh.
- Deterministic recommendation cards, rationale, duration, indicative cost, verification caveats and review-before-save behaviour.
- Planning-window and buffer settings, non-overlapping candidate slots, explicit slot choice, saving with start/end times and reload persistence.
- No-slot handling, separate **Add without a time** behaviour and duplicate recommendation UI prevention.
- Controlled AI upstream failure and member-vs-owner access checks through the actual API.

See [docs/BROWSER_E2E_TEST_PLAN.md](docs/BROWSER_E2E_TEST_PLAN.md) for scenario IDs, expected results, local setup, CI design, troubleshooting and limitations.

#### CI behaviour

The dedicated **Chromium browser end-to-end tests** job provisions PostgreSQL 16, applies migrations 001–017, installs Playwright Chromium, starts the deterministic AI fixture, starts the Platform API with test-only JWT settings and runs the browser suite against Vite. The job uploads the HTML report, failure screenshots, traces and service logs even when tests fail. Existing web typecheck/build, AI API, Platform API build, Platform API unit tests, PostgreSQL integration tests and migration-validation jobs remain enabled.

#### Known limitations and remaining manual checks

Chromium is the only automated browser project in Stage 45. Firefox, WebKit, mobile layouts, keyboard-only accessibility and assistive-technology tests remain manual/future work. The deterministic AI fixture validates the contract and error path, not live model quality, provider pricing, latency or availability. External mapping, weather, routing, opening-hours and flight providers are not verified by these browser journeys. Production deployment, HTTPS, secrets handling, real-provider AI verification and full operational recovery still need review.



### Stage 46 — finance and accessibility browser coverage

Stage 46 extends the existing Playwright suite; it does not replace Stage 45's authentication, trip, itinerary, recommendation or scheduling journeys. New browser tests exercise the existing shared-finance features through the real React app, authenticated Platform API and disposable PostgreSQL 16 database.

Coverage added:
- Two real test accounts, an owner-created shared expense with equal shares, and verification of the resulting debtor/creditor settlement.
- The debtor submits a repayment; the debtor cannot self-confirm it; the recipient confirms it; and the final settlement balances return to zero.
- Receipt upload rejects non-image content, supports a manual-review path when no receipt OCR provider is configured, allows traveller confirmation of merchant/date/amount/currency and expense association, verifies authenticated original-file retrieval, and detects an identical second upload.
- axe-core accessibility scanning of the registration screen against WCAG 2.1 A/AA rules, failing CI on critical or serious violations.

Only the existing AI provider adapter remains mocked for the browser suite. Finance endpoints, authentication, receipt storage and database writes are real. Receipt images are synthetic test fixtures; files are stored only in the workflow's temporary private receipt directory and are not committed.

#### Strictly typecheck and run E2E tests

From `apps/web`:

```bash
npm install
npx playwright install chromium
npm run typecheck
npm run typecheck:e2e
npm run build
npm run test:e2e
```

The `test:e2e` script runs `typecheck:e2e` before launching Chromium. CI's existing browser job therefore fails on TypeScript errors in the Playwright specs as well as browser assertion failures. The existing disposable PostgreSQL 16, test-only JWT settings, real Platform API and deterministic AI fixture configuration from Stage 45 are unchanged.

For the expanded two-account suite, set `RateLimits__AuthPerMinute=30` in the **test-only** Platform API environment to avoid shared CI IP collisions during repeated registrations and sign-ins. The application retains its normal default of 10 requests per minute when the override is absent. Do not carry the test override into production.

**Latest Stage 46 validation:** [GitHub Actions run #44500311672](https://github.com/harini281/travel_os/actions/runs/44500311672) completed successfully. All seven CI jobs passed, and the combined Stage 45–46 Chromium suite reported **7 passed** (four original browser journeys and three new finance/accessibility journeys). The workflow uploaded the Playwright result bundle as artifact `travelos-playwright-e2e-results`.

See [docs/BROWSER_E2E_TEST_PLAN.md](docs/BROWSER_E2E_TEST_PLAN.md) for the Stage 46 scenario IDs, setup, diagnostics and remaining limitations.


### Stage 47 — cross-browser, mobile and broader accessibility checks

Stage 47 expands the Playwright browser matrix while preserving the full Stage 45–46 Chromium journeys.

Automated browser projects:
- **Desktop Chromium:** the complete authentication, itinerary, AI, scheduling, finance, receipts and permissions suite, plus the cross-browser smoke journey.
- **Desktop Firefox and WebKit:** a shared browser-engine smoke journey that registers a traveller, creates a trip and timed itinerary activity, verifies persistence after refresh, checks for horizontal overflow and captures uncaught page errors.
- **Pixel 7 Chromium and iPhone 14 WebKit:** responsive journeys for registration, dashboard, trip cards and itinerary activity creation.
- **Tablet Chromium (768 × 1024):** the same responsive journey on a tablet-sized touch viewport.

Responsive journeys check document width against the active viewport at each major screen and run axe-core WCAG 2.1 A/AA scans on registration, dashboard and itinerary states. A keyboard journey verifies Tab focus visibility, reverse navigation and Enter activation. The application now has a consistent high-contrast `:focus-visible` outline for links, buttons, inputs, selects, textareas and tab-indexed controls.

#### Run the matrix locally

From `apps/web`:

```bash
npm install
npx playwright install chromium firefox webkit
npm run typecheck
npm run typecheck:e2e
npm run build
npm run test:e2e
```

Run the subsets independently:

```bash
npm run test:e2e:desktop
npm run test:e2e:responsive
```

The same disposable PostgreSQL 16 database, test-only API configuration and deterministic AI fixture from Stages 45–46 are required. The full setup is documented in [BROWSER_E2E_TEST_PLAN.md](docs/BROWSER_E2E_TEST_PLAN.md).

#### CI behaviour

The browser workflow installs Chromium, Firefox and WebKit with Linux dependencies. It runs the complete existing Chromium suite, desktop Firefox/WebKit journeys, and Pixel/iPhone/tablet responsive projects in the same isolated PostgreSQL-backed job. Screenshots, traces, videos, HTML reports and service logs are uploaded when the run finishes, including on failures.

#### Stage 47 scope and limitations

This stage adds automated engine coverage and responsive/automated accessibility checks, not a formal accessibility certification. axe checks the named registration, dashboard and itinerary states; it does not scan every dynamic panel or every route. Keyboard coverage checks a small core trip-planning interaction, not every control. Human screen-reader testing, full keyboard-only workflow testing, zoom/reflow review, high-contrast mode, Android/iOS physical devices, Safari versions outside Playwright's WebKit build and real assistive technology remain manual verification items.
 
**Stage 47 validation:** [GitHub Actions run #44510651024](https://github.com/harini281/travel_os/actions/runs/44510651024) completed successfully on commit `4fe728cb80625e0ced82c959520f0c708451b51d`. All seven CI jobs passed. The combined Chromium, Firefox, WebKit, Pixel 7, iPhone 14 and tablet Playwright matrix reported **16 passed in 2.7 minutes**. Platform API unit tests reported 10 passed and 14 PostgreSQL-dependent cases skipped in the unit-only job; the separate PostgreSQL integration job passed all 24 tests. AI API tests reported 18 passed with one warning. The browser report, screenshots/traces and service logs are retained in artifact [`travelos-playwright-e2e-results` #14523596375](https://github.com/harini281/travel_os/actions/runs/44510651024/artifacts/14523596375).


## 8. What is still left?

TravelOS has a substantial feature set, but a successful CI run is not a production launch. The main remaining work is:

1. **Merge and release Stage 39:** review and merge [PR #41](https://github.com/harini281/travel_os/pull/41) into the intended integration branch after review.
2. **End-to-end testing:** Stages 45–47 automate Chromium journeys, Firefox/WebKit smoke coverage, mobile/tablet responsive journeys, shared finance, receipts and targeted axe-core WCAG 2.1 A/AA checks. Full assistive-technology and production-browser verification remain manual.
3. **Live AI verification:** test Stage 39 with a real configured provider key, check cost/latency, rate limits, malformed responses and user-facing errors. Current CI tests use a mocked provider; they do not call OpenAI.
4. **Production deployment:** configure HTTPS, managed PostgreSQL, persistent private receipt storage, secrets management, backups, logging, monitoring and a deployment pipeline.
5. **Authentication hardening:** email verification, password reset, refresh-token/session management and MFA are not implemented.
6. **External service readiness:** replace or contractually approve public/demo map, routing, geocoding and opening-hours services for production scale; configure FlightAware only if needed and review its usage charges.
7. **Notifications delivery:** the current reminder inbox is in-app; server scheduling and push/email/SMS delivery remain future work.
8. **Booking integrations:** live inventory, supplier confirmations, payments and actual flight/hotel/activity booking are not implemented.

**Remaining work count:** 8 major workstreams above. This is a practical release checklist, not eight small coding tasks; some contain multiple implementation and review tasks.

## 9. Security and operational notes

- Never commit `.env`, API keys, database passwords or JWT signing keys.
- Keep OpenAI and FlightAware credentials on the server only.
- Use a unique JWT signing secret (32+ bytes) and HTTPS outside local development.
- Back up PostgreSQL before applying migrations or resetting local volumes.
- Keep uploaded receipts in private persistent storage, outside the web root.
- Public OpenStreetMap/OSRM/Nominatim/Overpass services have usage policies and no production SLA; use an appropriate provider for commercial scale.
- AI output, weather assessments, route estimates and opening-hours data are advisory. Verify important travel decisions with providers.

## 10. Repository structure

```text
apps/web                  React + TypeScript + Vite frontend
services/platform-api     ASP.NET Core 8 platform API
services/ai-api           FastAPI AI service
database/migrations       Ordered PostgreSQL SQL migrations
docs                      Architecture and feature test plans
packages/contracts        Shared/versioned API contracts
tests                     AI and Platform API automated test suites
infrastructure            Deployment and infrastructure assets
.github/workflows         GitHub Actions CI
```

## Links

- Repository: https://github.com/harini281/travel_os
- Stage 39 pull request: https://github.com/harini281/travel_os/pull/41
- CI workflow: [TravelOS CI](.github/workflows/ci.yml)
- AI recommendation test plan: [AI_ACTIVITY_RECOMMENDATIONS_TEST_PLAN.md](docs/AI_ACTIVITY_RECOMMENDATIONS_TEST_PLAN.md)
- Browser end-to-end test plan: [BROWSER_E2E_TEST_PLAN.md](docs/BROWSER_E2E_TEST_PLAN.md)


### Stage 42 — Recommendation scheduling tests

The scheduling slot calculation has been extracted into a deterministic, independently testable planner. A .NET xUnit test project now covers:
- Slots before, between and after saved timed activities.
- No-fit planning windows.
- Overlapping saved activities and conflict warnings.
- Activities without times and the corresponding warning.
- Sorting activities before gap calculation.
- Invalid planning windows, durations and buffers.

GitHub Actions now runs the Platform API test project as a separate CI job. These tests cover the scheduling calculation; they do not replace database-backed authorization/API integration tests or manual browser verification.

Run locally from the repository root:

```bash
dotnet test tests/platform-api-tests/TravelOS.PlatformApi.Tests.csproj --configuration Release
```


### Stage 43 — PostgreSQL-backed scheduling API integration tests

Added an ASP.NET Core integration-test host and HTTP-level tests for the recommendation scheduling endpoint. The tests cover authentication, owner-only access, successful slot generation, no persistence by the suggestion endpoint, approved-itinerary rejection, missing-itinerary rejection and invalid-duration rejection.

GitHub Actions provisions a disposable PostgreSQL 16 database, applies the repository migrations, and runs these tests against the real platform API and PostgreSQL provider.

To run locally, create a disposable test database and apply all migrations listed in `.github/workflows/ci.yml`, then set:

```bash
TRAVELOS_TEST_CONNECTION_STRING="Host=localhost;Port=5432;Database=travelos_test;Username=postgres;Password=your-test-password"
dotnet test tests/platform-api-tests/TravelOS.PlatformApi.Tests.csproj --configuration Release
```

The integration tests are skipped when `TRAVELOS_TEST_CONNECTION_STRING` is not configured; the scheduling planner unit tests still run normally. Never point this variable at a production or personal database.


### Stage 44 — itinerary workflow API integration tests

Extended the PostgreSQL-backed Platform API suite beyond recommendation scheduling. The tests now exercise owner-only itinerary creation, day creation, timed activity creation, activity updates/deletion, itinerary approval and read-only enforcement after approval. They also verify trip members cannot add days or activities and duplicate itinerary creation is rejected.

Run with the Stage 43 PostgreSQL test setup using `TRAVELOS_TEST_CONNECTION_STRING` as documented above. The CI integration job runs the complete suite against PostgreSQL 16 after applying all migrations.

## Stage 48 — Container deployment foundation

Stage 48 adds Docker builds and a Docker Compose deployment for the React web client, .NET 8 Platform API, FastAPI AI service and PostgreSQL 16. It includes Nginx same-origin API proxying, private persistent receipt storage, isolated database networking, health checks and an ordered migration runner.

**Local container deployment (requires Docker Engine and Docker Compose v2):**

```bash
cp deploy/production.env.example deploy/production.env
# Edit deploy/production.env: set a unique database password,
# a random JWT signing key (at least 32 bytes), and your HTTPS web origin.
docker compose --env-file deploy/production.env -f docker-compose.prod.yml config
docker compose --env-file deploy/production.env -f docker-compose.prod.yml up --build -d --wait
docker compose --env-file deploy/production.env -f docker-compose.prod.yml ps
```

The web app is bound to `127.0.0.1:8088` by default. Configure a trusted TLS reverse proxy or managed ingress before public access. PostgreSQL and the API services are not published directly to the host. Database and receipt data use named volumes.

**Important:** `deploy/production.env.example` contains placeholders, not production credentials. Copy it to `deploy/production.env`, replace every placeholder and never commit the resulting file. Keep AI integrations in demo mode until provider access, privacy and costs have been reviewed.

CI now validates the Compose configuration, builds all three application images, starts the complete disposable stack, checks web/API/AI health and verifies that all 18 ordered migrations were recorded. See [the Stage 48 deployment test plan](docs/PRODUCTION_DEPLOYMENT_TEST_PLAN.md).

This is a container deployment foundation, not a public production launch. DNS/TLS hosting, managed secrets, backups and restore drills, vulnerability scanning, centralized observability, load testing and operational review remain required.


## Stage 49 — Production security hardening

Stage 49 tightens production startup configuration, browser/API response headers and Docker network boundaries. Production startup now requires explicit JWT issuer/audience values, a non-placeholder signing key of at least 32 bytes and exact HTTPS CORS origins. The web container is isolated from the AI API and database; the Platform API bridges the frontend, backend and data networks. CI verifies the security headers, host port exposure and network separation.

See [docs/PRODUCTION_SECURITY_HARDENING_TEST_PLAN.md](docs/PRODUCTION_SECURITY_HARDENING_TEST_PLAN.md). This is hardening groundwork, not a security certification or production launch.

## Stage 50 — Production readiness and release preparation

Stage 50 separates the privileged PostgreSQL migration administrator from the non-superuser API database role, validates that runtime role at API startup, ignores deploy/production.env, validates live AI-provider configuration at AI service startup, and provides encrypted database/receipt backup utilities with archive integrity checks. Compose now bounds local JSON-file log growth. These controls improve release preparation; they do not create hosting infrastructure or make the application production-ready.

Operational guidance:
- Production operations runbook: docs/PRODUCTION_OPERATIONS_RUNBOOK.md
- Production release readiness checklist: docs/PRODUCTION_RELEASE_READINESS_CHECKLIST.md

Create encrypted backups with deploy/backup.sh to an output directory outside the repository. Verify an encrypted backup with deploy/verify-backup.sh. The verification command checks checksums, decryption and archive structure; it is not a substitute for a full restore drill into an isolated PostgreSQL database and receipt-storage directory.

Stage 50 does not deploy publicly, change production credentials, select a cloud provider, provision infrastructure or merge Stage 49 PR #51. Managed secrets, TLS ingress, off-host backup retention, restore drills, centralized observability, vulnerability scanning/SBOM, external-provider/privacy approvals, load testing and formal release sign-off remain explicit production gates.
