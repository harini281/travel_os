# Stage 38 — contextual trip assistant verification

## Access and isolation
1. An unauthenticated request to the briefing endpoint returns 401.
2. An unauthenticated request to the ask endpoint returns 401.
3. A trip owner can retrieve the briefing and ask questions.
4. A trip member can retrieve the briefing and ask questions.
5. A non-member receives 404 and cannot infer trip contents.
6. Expense, weather, flight, budget-alert and itinerary context is scoped to the requested trip.
7. Notifications are additionally scoped to the signed-in user.

## Briefing rules
8. Missing itinerary days produce an itinerary planning insight.
9. Empty upcoming itinerary days are reported.
10. Upcoming activities without complete start/end times produce a schedule insight.
11. Overlapping saved activity times produce a schedule warning.
12. Latest saved weather assessments are selected per activity/date; only current and future activity dates are loaded.
13. Moderate/high weather assessments produce caution/warning insights.
14. Flight delay signals of 30 minutes or more and saved cancellation statuses produce a warning.
15. Missing flight records produce an informational prompt to add flight details.
16. Recorded expenses are compared with the budget only when currencies match.
17. Expenses in other currencies are explicitly excluded from the budget comparison; no conversion is assumed.
18. Recent saved budget alerts appear in the briefing.
19. Due, unread scheduled notifications produce a reminder insight.
20. A trip with no high-priority signals receives a neutral positive summary.
21. Briefing includes trip dates, days-until-start, days-remaining, generated timestamp and data freshness disclaimer.

## Question and answer
22. Questions shorter than 2 characters or longer than 500 characters are rejected.
23. Weather/forecast questions use saved weather assessments and state when none exist.
24. Flight/airport/delay questions use saved flight records and warn that missing status is not confirmation of on-time operation.
25. Budget/cost questions report the saved budget and same-currency recorded expenses.
26. Schedule/itinerary questions report activity counts, untimed activities and possible overlaps.
27. Reminder/notification questions return the signed-in user's unread notifications.
28. General questions return a trip overview and top briefing insights.
29. The answer includes suggested follow-up questions and an explicit no-live-search/no-external-AI disclaimer.

## Workspace
30. Opening a trip loads the briefing alongside the existing trip data when online.
31. Refresh reloads the briefing.
32. Offline mode does not request a server briefing and explains that a connection is required.
33. Quick-question buttons populate the ask flow and return answers.
34. Manual questions display the category, answer, supporting facts and disclaimer.
35. The briefing and question interface remain usable at narrow viewport widths.

## Explicit limitations
- Stage 38 is deterministic, keyword-routed question answering over saved TravelOS data; it is not a generative LLM chat.
- It does not perform live web searches or contact external providers.
- Flight and weather data may be stale and are not travel guarantees.
- Currency totals are not converted.
- Suggestions do not modify itineraries, book travel, or contact providers.
