# Airport ground transfer — verification plan

## API
1. Unauthenticated requests return 401.
2. Non-members cannot estimate transfers for another trip (404).
3. Invalid IATA code, destination length and pickup buffer return validation errors.
4. Valid airport/destination inputs return route distance, route duration, provider and estimate timestamp.
5. Missing geocoding matches or OSRM route returns 404 with a route-not-found explanation.
6. Mapping provider HTTP errors return 502; timeouts return 504.
7. More than five estimates per user per minute are rate limited.
8. No transfer booking, persistent trip mutation or payment is performed.

## UI
- Open a trip and enter airport code plus destination.
- Confirm driving and cycling modes can be selected.
- Confirm distance, road duration, pickup buffer and planning departure time render.
- Confirm error states are readable and no fake result appears.
- Confirm the disclaimer explains live traffic and airport processing are excluded.

## Provider limitations
Nominatim search results may be ambiguous and public endpoints have usage policies. OSRM's public demo routing endpoint is not an SLA-backed production service and generally provides route estimates without live traffic. Production deployment should use a contracted geocoder/routing provider or self-hosted services, with airport coordinates verified against a maintained airport dataset. Estimates are not bookings or guaranteed arrival times.
