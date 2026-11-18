# Route-aware itinerary analysis — verification plan

## API access and validation
1. Unauthenticated requests return 401.
2. A user who is not a trip member receives 404.
3. The default route buffer is 15 minutes.
4. Buffer values outside 0–180 return a validation error.
5. A trip with no itinerary returns an empty day list and a clear disclaimer.
6. Days with fewer than two timed activities return an explanatory message and no route legs.
7. Days with more than 25 timed activities return a validation error before calling the routing provider.
8. Activities without both saved coordinates are marked unavailable; no coordinates are fabricated.
9. A day with at least two located activities requests an OSRM driving distance/duration table.
10. A missing route result is marked unavailable and does not crash the response.
11. Provider HTTP errors return 502; provider timeouts return 504.
12. Route analysis is read-only and never modifies itinerary activities.

## Route feasibility
13. A negative time gap is marked schedule_conflict.
14. A non-negative gap shorter than road duration plus selected buffer is marked tight.
15. A gap equal to or longer than road duration plus selected buffer is marked feasible.
16. Distance is returned in kilometres and road duration in whole minutes.
17. A pair with missing coordinates is unavailable even if other stops on the day have coordinates.
18. Consecutive timed activities are analysed in chronological order.

## UI
19. The traveller can choose a 0, 15, 30, 45 or 60-minute extra buffer.
20. The route panel shows each leg, distance, road duration, available gap and required time.
21. Tight, conflicting, feasible and unavailable results have distinguishable labels.
22. Changing the buffer and rerunning analysis refreshes the displayed result.
23. Loading and provider error states are visible and no fabricated estimate appears.
24. The disclaimer states that OSRM estimates are not live traffic ETAs and exclude parking, queues, opening hours and public transport.

## Provider limitations
The public OSRM demo service is not an SLA-backed production routing service and does not provide live traffic. Use a contracted or self-hosted routing provider before production launch. Validate activity coordinates and geocoding results before relying on a route estimate.
