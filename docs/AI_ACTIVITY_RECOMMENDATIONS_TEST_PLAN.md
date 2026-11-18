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
