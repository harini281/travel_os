# Stage 39 — AI activity recommendations verification plan

## AI service checks

1. Run `pytest -q` from `services/ai-api`.
2. Confirm request validation rejects invalid currencies, oversized dates/context, invalid traveller counts and oversized questions.
3. Confirm omitted `travellerCount` defaults to one.
4. Confirm demo mode returns `demo_only` with an empty recommendation list; it must not fabricate suggestions.
5. Confirm OpenAI mode without an API key returns HTTP 503.
6. Confirm structured provider output is validated, limited to 1–6 suggestions, and costs use the requested trip currency.
7. Confirm timeout, refusal, malformed JSON and provider errors return controlled errors.
8. Confirm prompts prohibit claims about live opening hours, availability, verified prices, bookings, exact travel times or coordinates.

## Platform API checks

1. Run `dotnet build services/platform-api/TravelOS.PlatformApi.csproj --configuration Release`.
2. Confirm unauthenticated requests return 401.
3. Confirm non-members cannot request recommendations for another trip.
4. Confirm a missing itinerary returns 409 and a day number not present in the current itinerary returns a validation error.
5. Confirm focus is one of `all`, `weather`, `budget`, `group`, or `schedule`; traveller count is 1–30; question is at most 500 characters.
6. Confirm group interests are aggregated from saved trip-member preferences.
7. Confirm selected-day activities and the latest saved weather assessments are included in the AI context.
8. Confirm budget context compares only expenses and planned amounts in the trip budget currency; no currency conversion is implied.
9. Confirm the request uses the existing `ai-generation` rate limit.
10. Confirm demo/unconfigured AI returns 503 and provider failures return safe 502/503/504 responses.
11. Confirm no recommendation is persisted and no itinerary activity, time, budget, booking or approval state is changed.

## Workspace checks

1. Open a trip with an itinerary and select each available day.
2. Request recommendations using each focus and an optional natural-language question.
3. Confirm cards show category, rationale, indicative duration/cost, best-for context and verification note.
4. Confirm provider/model and generation timestamp are displayed.
5. Confirm recommendations clear when switching trips.
6. Confirm the existing AI full-itinerary draft/review/apply flow remains available.
7. Confirm an empty itinerary disables recommendation generation.
8. Run `npm run build` from `apps/web`.

## Manual scenarios

- Outdoor activity with a recent high-severity weather assessment.
- Day with several activities and a schedule-balance request.
- Group members with different saved interests.
- Budget with expenses in the trip currency.
- Budget with expenses in another currency; ensure no silent conversion occurs.
- Day without a weather assessment.
- AI service unavailable or configured in demo mode.

## Limitations

Recommendations are advisory, not verified venue listings. Weather assessments can become stale. Venue accessibility, opening hours, availability, route durations and prices are not independently verified. Verify details with authoritative providers before changing travel plans.

## Stage 40 — Traveller-approved recommendation saving

1. Generate recommendations as the trip owner for a day in an unapproved itinerary.
2. Select **Add to itinerary** on a recommendation and confirm a new activity is created on the selected day.
3. Confirm the saved activity retains the suggested title, reason/verification note, duration and estimated cost/currency where supplied.
4. Confirm no coordinates, place name or time are invented; the owner must complete those details.
5. Confirm the card changes to **Added to itinerary** after the itinerary reloads.
6. Try adding the same title again (including case differences); confirm the duplicate is not added.
7. Confirm the save action is unavailable when the itinerary is approved.
8. Confirm the API still enforces owner-only itinerary writes; a trip member cannot bypass the UI and add an activity.
9. Switch trips or selected days and confirm the saved state is based on the refreshed itinerary, not stale recommendation state.
10. Simulate a failed API request and confirm an error is shown and the recommendation remains available for retry.

The action is an explicit owner choice. It does not book a venue, verify provider data, assign coordinates or times, or approve the itinerary.


## Stage 41 — AI recommendation scheduling

### Scheduling API
1. Confirm unauthenticated schedule requests return 401.
2. Confirm only the trip owner can request recommendation slots; trip members and unrelated users cannot.
3. Confirm a missing itinerary or approved itinerary is rejected without changes.
4. Confirm an invalid day number, missing title, duration outside 15–720 minutes, invalid time window, or buffer outside 0–240 minutes is rejected.
5. Confirm candidate slots are generated only inside the owner-supplied planning window.
6. Confirm slots do not overlap saved timed activities and include the selected buffer around adjacent activities.
7. Confirm nested/overlapping existing activities are reported as conflicts and a warning is returned.
8. Confirm untimed saved activities are explicitly disclosed as excluded from time calculations.
9. Confirm the endpoint is read-only: no activity is inserted and no itinerary state changes.

### Workspace
1. As the trip owner, generate Stage 39 recommendations for an editable itinerary day.
2. Set the planning window and buffer; request a suitable time for a recommendation with a suggested duration.
3. Confirm the slot list displays start/end time and the reason it fits between existing activities.
4. Select a slot and explicitly choose **Save with this time**.
5. Confirm the new activity stores the selected start/end times and suggested duration, and the itinerary refreshes.
6. Confirm **Add without a time** remains a separate explicit action.
7. Confirm no suitable slot is shown as such when the planning window has insufficient capacity.
8. Confirm approved itineraries cannot be scheduled or edited.
9. Confirm members do not see owner scheduling controls and cannot call the endpoint successfully.
10. Confirm existing Stage 39 recommendation generation and Stage 40 save behaviour still work.

Slots use saved activity times plus the owner-entered planning window and buffer. They do not verify travel duration, traffic, venue opening hours, provider availability or live conditions. No changes are persisted by the suggestion endpoint.


## Stage 42 — automated recommendation scheduling tests

Run from the repository root:

```bash
dotnet test tests/platform-api-tests/TravelOS.PlatformApi.Tests.csproj --configuration Release
```

The xUnit suite validates:
1. Candidate slots before, between and after existing timed activities.
2. A planning window that cannot fit the requested duration.
3. Existing overlaps, exact overlap minutes and conflict warnings.
4. Missing activity times and their warning.
5. Sorting unsorted activities before gap calculation.
6. Invalid planning windows, durations below 15 or above 720 minutes, and buffers below 0 or above 240 minutes.

The planner is a pure calculation component; this suite does not test HTTP authentication, database ownership checks, EF Core persistence or browser interaction. Those remain integration/manual verification tasks.


## Stage 43 — PostgreSQL-backed API integration tests

The integration tests use `WebApplicationFactory<Program>` and a disposable PostgreSQL database. GitHub Actions provisions PostgreSQL 16 and applies all migrations before running the tests.

Covered HTTP scenarios:
1. An unauthenticated request receives 401.
2. A trip member receives 404 from the owner-only scheduling endpoint.
3. The owner receives a valid slot for an editable itinerary.
4. The suggestion request leaves the saved activity count unchanged.
5. An approved itinerary receives 409.
6. A trip without an itinerary receives 409.
7. An invalid duration receives 400.

To run locally, use a disposable PostgreSQL database with all migrations applied:

```bash
TRAVELOS_TEST_CONNECTION_STRING="Host=localhost;Port=5432;Database=travelos_test;Username=postgres;Password=your-test-password"
dotnet test tests/platform-api-tests/TravelOS.PlatformApi.Tests.csproj --configuration Release
```

When `TRAVELOS_TEST_CONNECTION_STRING` is absent, PostgreSQL-backed tests are skipped; pure planner unit tests continue to execute. Never use a production database for integration tests.


## Stage 44 — itinerary workflow API integration tests

Additional PostgreSQL-backed HTTP scenarios:
1. Duplicate itinerary creation returns 409.
2. The owner can add a day inside the trip date range and it is persisted.
3. A trip member cannot add a day.
4. The owner can add an activity with start/end times and those values are persisted.
5. A trip member cannot add an activity.
6. The owner can approve an itinerary containing activities and the approver/timestamp are persisted.
7. Approved itineraries reject further activity additions with 409.
8. The owner can update and delete an activity while the itinerary remains editable.

Run the suite using the disposable PostgreSQL setup documented in the Stage 43 section. The same CI job now executes the Stage 42 planner tests, Stage 43 scheduling API tests and Stage 44 itinerary workflow tests.
