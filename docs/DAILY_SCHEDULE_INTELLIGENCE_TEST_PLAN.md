# Daily schedule intelligence verification

1. Call the endpoint unauthenticated; expect 401.
2. Call for a trip where the caller is not a member; expect 404.
3. Verify default minimum gap is 30 minutes.
4. Verify minimumGapMinutes outside 0–240 returns validation error.
5. Verify no itinerary returns an empty days list with a clear disclaimer.
6. Verify unscheduled activities are counted but excluded from gap comparisons.
7. Verify non-overlapping consecutive activities produce short_gap or buffer_met classifications based on the selected buffer.
8. Verify every overlapping pair is returned, not only adjacent activities in start-time order.
9. Verify nested overlap example: A 09:00–12:00, B 10:00–11:00, C 11:30–13:00 returns A/B and A/C conflicts (two pairs), without reporting B/C as overlapping.
10. Verify cascading overlap example: A 09:00–10:30, B 10:00–11:30, C 11:00–12:00 returns A/B, A/C and B/C conflicts (three pairs).
11. Verify a 15/30/45/60-minute buffer selection changes short-gap classifications as expected.
12. Verify the dashboard renders each returned conflict pair and its overlap duration, while keeping short-break records distinct.
13. Verify schedule analysis is read-only and does not reorder or modify activities.
14. Confirm the disclaimer makes clear that clock-time gaps are not route, traffic, opening-hours or transfer-duration estimates.
15. Verify schedule suggestions preserve each activity's original duration.
16. Verify a nested conflict proposes later times for the conflicting activities in chronological order.
17. Verify a cascading conflict proposes a sequence with the selected buffer between activities.
18. Verify an activity that cannot fit before midnight is marked cannot_fit with a manual-review reason.
19. Verify suggestions are returned as proposals only and do not update saved activity times.
20. Verify activities without times remain unscheduled and are never assigned invented times.

21. Verify that when one activity cannot fit before midnight, every later activity in the proposed sequence is also flagged for manual review rather than receiving an unsafe time.
22. Verify end times earlier than or equal to start times are rejected by the activity API and cannot enter optimization.
23. Verify an activity already meeting the selected buffer is left at its original time.
24. Verify the optimizer never proposes an earlier start time than the saved activity time.

25. Verify only the trip owner can apply a schedule suggestion and approved itineraries expose no apply action.
26. Verify applying a suggestion updates only that activity's start and end times and preserves title, place, coordinates, duration, cost, currency and sort order.
27. Verify stale suggestions are rejected when an activity's saved times no longer match the original times used by the analysis.
28. Verify the schedule analysis is reloaded after an approved adjustment and reflects any remaining conflicts.
29. Verify only one apply action can run at a time and the UI reports success or failure.
