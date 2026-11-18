# Attraction opening-hours — verification plan

## API access and limits
1. Unauthenticated requests return 401.
2. A non-member receives 404.
3. Trips without an itinerary return an empty result with a disclaimer.
4. Requests containing more than 40 activities return a validation error.
5. Provider errors return 502; provider timeouts return 504.
6. The endpoint is rate limited and does not write to the itinerary database.

## Place matching
7. Activities without a place name or coordinates return missing_location.
8. Activities without start or end times return missing_schedule.
9. Matching requires a same-name OpenStreetMap place within 250 metres of saved coordinates.
10. No nearby match returns unavailable and does not invent a venue.
11. A matched place without an opening_hours tag returns no_hours_data.
12. Ambiguous or unsupported opening-hours expressions return manual_review.

## Weekly schedule interpretation
13. A visit entirely within a published simple time interval returns open_during_activity.
14. A visit that begins before opening or ends after closing returns outside_opening_hours.
15. An explicit off/closed day returns closed.
16. A day not covered by a day-specific weekly schedule is treated as closed under that published schedule.
17. 24/7 covers the full TimeOnly day.
18. Split periods such as 09:00-12:00,13:00-17:00 are interpreted as separate intervals.
19. Day ranges such as Mo-Fr and wrapped ranges such as Fr-Mo are interpreted correctly.
20. Public-holiday (PH), school-holiday (SH), sunrise/sunset, overnight, seasonal and unsupported expressions return manual_review rather than a guessed status.
21. Conflicting or multiple matching rules return manual_review.

## UI
22. The dashboard displays each activity's status, matched place, published hours and recommendation.
23. The user can distinguish verified-within-published-hours from closed, outside-hours and unknown/manual-review states.
24. Loading and provider error states are visible; no placeholder result is presented as a confirmed status.
25. The disclaimer explains community-data limitations and the need to verify important visits with the venue.

## Provider limitations
OpenStreetMap opening_hours tags are community-maintained and may be missing, stale or incomplete. Overpass is a public query service, not an SLA-backed production dependency. Production use should adopt an appropriate hosted or self-managed OSM data service and respect its usage policy.
