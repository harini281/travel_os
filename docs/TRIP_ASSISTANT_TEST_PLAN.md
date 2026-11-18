# Stage 38 — contextual trip assistant verification

## Access and isolation
1. An unauthenticated request to the trip briefing endpoint returns 401.
2. A signed-in trip owner can retrieve the briefing.
3. A trip member can retrieve the briefing.
4. A user who is not a member receives 404 and cannot infer trip contents.
5. All expense, weather and notification records in the response belong only to the requested trip; notifications are additionally scoped to the signed-in user.

## Context aggregation
6. A trip without an itinerary returns a null itinerary summary and a planning insight.
7. An itinerary with empty future days reports those days.
8. Upcoming activities without start or end times produce a schedule insight.
9. Budget totals and recorded expenses are grouped by currency; comparisons are made only for the budget currency.
10. A missing budget produces a budget setup insight.
11. Cancelled or diverted flights produce a disruption insight.
12. A flight departing within 48 hours produces an upcoming-flight insight.
13. Latest weather assessments are selected per activity/date and only future/current dates are included.
14. High-severity and moderate weather assessments produce appropriately worded review insights.
15. Only the signed-in user's active, due notifications are included.
16. If no planning signals are found, the briefing returns a positive maintenance insight.
17. The response includes a timestamp and explicit data freshness / decision-support disclaimer.

## Workspace
18. Opening a trip loads its assistant briefing alongside other trip data while online.
19. The refresh action reloads the briefing.
20. Offline mode does not request a server briefing and explains that a connection is required.
21. The summary shows activity, flight, weather and unread-notification counts.
22. Insights and snapshots render when their underlying data is empty or absent.
23. The panel is responsive on narrow screens.

## Explicit limitations
- Stage 38 uses deterministic rules over saved TravelOS data; it is not a generative LLM chat.
- Flight and weather facts may be stale and are not live guarantees.
- Currency totals are not converted. Expenses in currencies other than the trip budget currency are shown separately.
- Suggestions are decision-support prompts and do not edit the itinerary, book travel, or contact providers.
