# Weather disruption intelligence — verification plan

## Automated CI
The repository CI must pass:
- React TypeScript production build.
- ASP.NET Core .NET 8 restore and Release build.
- FastAPI regression suite.
- PostgreSQL migrations 001 through 013 against a clean PostgreSQL 16 database.

## API and data scenarios

1. **Trip access** — unauthenticated requests return 401; users outside the trip receive 404 for both history and scan routes.
2. **No itinerary** — scan returns 409 with `itinerary_missing`.
3. **No activities** — scan returns `no_activities` without calling the weather provider.
4. **Missing coordinates** — the activity appears in `skippedActivities`; no weather assessment is persisted for it.
5. **Forecast date available** — an activity with coordinates and a date returned by Open-Meteo receives one persisted assessment with source, retrieval timestamp and weather metrics.
6. **Date outside forecast range** — the activity is skipped with an explicit forecast-window reason; no weather is extrapolated.
7. **Provider unavailable or timeout** — affected activities are skipped; the API does not fabricate a clear-weather result.
8. **Partial provider failure** — successful locations are persisted and failed locations appear in the skipped list.
9. **Multiple activities at one coordinate** — one provider request is reused for the coordinate group and each matching activity is assessed for its own date.
10. **Limits** — more than 40 activities or 12 unique coordinate groups returns 409; a user exceeding four scans per minute receives 429.
11. **History** — GET returns the latest saved assessment for each activity/date, not every repeated scan.
12. **No itinerary mutation** — compare itinerary activity rows before and after scanning; IDs, coordinates, titles, dates and approval state must remain unchanged.

## Manual UI checks

- Open a trip containing an itinerary with geocoded activities and select **Scan trip weather**.
- Confirm the UI shows severity, forecast metrics, recommendation, retrieval time and Open-Meteo attribution.
- Confirm skipped activities explain missing coordinates, provider failures or unavailable forecast dates.
- Open the same trip again and confirm saved assessments load.
- Confirm the scan button is disabled when no itinerary or activities exist.
- Confirm mobile layout stacks assessment cards and scan controls.

## Important interpretation
Severity labels are TravelOS heuristics based on daily forecast signals. They are not official warnings, safety guarantees or a substitute for local authorities. The feature does not cancel, move or replace itinerary activities.
