# Airport and connection intelligence — verification plan

## Connection assessment scenarios

1. No flights in a trip returns an empty connection list.
2. One flight in a journey group returns no connection pair.
3. Two chronological flights in the same group are paired.
4. Flights in different groups are never paired, even if adjacent by time.
5. An inbound arrival at the same IATA code as the outbound departure is treated as same-airport.
6. Different IATA codes produce `airport_change`; the response does not invent a ground-transfer duration.
7. A gap below zero minutes produces `overlap`.
8. Gaps of 0–59, 60–119, 120–179 and 180+ minutes produce `very_tight`, `tight`, `moderate` and `more_time` respectively.
9. Cancelled or diverted legs produce `unassessable`.
10. Effective inbound arrival uses actual arrival, then estimated arrival, then scheduled arrival.
11. Effective outbound departure uses actual departure, then estimated departure, then scheduled departure.
12. Provider time usage is labelled separately from schedule-only assessment.
13. A trip non-member receives 404; unauthenticated access receives 401.
14. The connection assessment endpoint performs no writes and does not change flight or itinerary records.

## Traveller UI scenarios

- Add two legs to the same journey group, for example Outbound.
- Add a return flight using a separate Return group and verify the groups are not joined.
- Verify connection gap, airport codes, explanation and risk label render.
- Change the inbound destination and verify an airport-change warning.
- Verify a cancelled/diverted flight does not show a reassuring connection label.
- Verify narrow-screen layout.

## Product limitations

This release does not use official airline minimum connection time (MCT) data. It does not know terminals, immigration, visa eligibility, baggage reclaim/recheck, security re-screening, boarding cutoff, airport transfer time, separate-ticket risk or protected-connection status. The thresholds are heuristic product signals only. No connection is guaranteed and no booking or itinerary is modified.
