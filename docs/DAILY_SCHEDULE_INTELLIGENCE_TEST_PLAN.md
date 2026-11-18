# Daily schedule intelligence verification

1. Call the endpoint unauthenticated; expect 401.
2. Call for a trip where the caller is not a member; expect 404.
3. Verify default minimum gap is 30 minutes.
4. Verify minimumGapMinutes outside 0–240 returns validation error.
5. Verify no itinerary returns an empty days list with a clear disclaimer.
6. Verify unscheduled activities are counted but excluded from gap comparisons.
7. Verify consecutive timed activities produce overlap, short_gap or buffer_met classifications.
8. Verify nested overlaps are counted across all overlapping pairs.
9. Verify a 15/30/45/60-minute buffer selection changes classifications as expected.
10. Verify UI shows recommendations and the explicit no-route-estimate disclaimer.
11. Verify schedule analysis is read-only and does not reorder or modify activities.
