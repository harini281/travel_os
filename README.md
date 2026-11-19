# TravelOS

**The Intelligent Operating System for Travel**

TravelOS is a multi-service travel-planning platform built with React, ASP.NET Core, PostgreSQL and Python/FastAPI. It brings trip planning, group coordination, budgets, itinerary intelligence and AI-assisted recommendations into one workspace.

> **Current development status:** Stage 42 — automated tests for AI recommendation scheduling. Stages 40 and 41 remain under review in separate draft pull requests. This is an active development project, not yet a production-ready booking platform.

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

There are **two example environment files** to copy. Do not commit real secrets.

| File | What it configures | Required? |
|---|---|---|
| `.env.example` → `.env` (repository root) | Local PostgreSQL container values and reference settings for the API/AI services | Yes for the documented Docker setup |
| `apps/web/.env.example` → `apps/web/.env.local` | Browser's platform API URL | Yes |
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
| `Receipts__StoragePath` | Private receipt storage directory |
| `FLIGHTAWARE_API_KEY` | Optional FlightAware AeroAPI key for live flight refresh |

### AI service settings

Set these in the terminal where Uvicorn is started. The Python service does **not** automatically read the repository root `.env` file.

| Variable | Purpose |
|---|---|
| `AI_PROVIDER` | `demo` (default) or `openai` |
| `OPENAI_API_KEY` | Required only when enabling OpenAI itinerary generation/recommendations |
| `OPENAI_MODEL` | Optional model override; example: `gpt-4.1-mini` |
| `AI_RECEIPT_PROVIDER` | Receipt extraction provider; keep `demo` unless deliberately configured |

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

Open a new terminal:

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

Open another terminal from the repository root:

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

## 8. What is still left?

TravelOS has a substantial feature set, but a successful CI run is not a production launch. The main remaining work is:

1. **Merge and release Stage 39:** review and merge [PR #41](https://github.com/harini281/travel_os/pull/41) into the intended integration branch after review.
2. **End-to-end testing:** add automated API/integration tests for authentication, trip permissions, itinerary edits, group expenses, recommendations and provider failure cases; manually test the full browser workflows.
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
