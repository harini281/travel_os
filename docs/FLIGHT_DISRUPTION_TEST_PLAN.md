# Flight and journey disruption — verification plan

## Automated CI
- React typecheck and production build.
- ASP.NET Core .NET 8 restore and Release build.
- FastAPI regression tests.
- PostgreSQL 16 applies migrations 001–014 on an empty database.

## API and data scenarios

1. Unauthenticated list/create/refresh requests return 401.
2. A non-member cannot list, create or refresh flights for another trip (404).
3. A valid member can save a flight using a number with or without spaces, three-letter IATA codes and local datetime inputs converted by the browser to UTC.
4. Invalid airport codes, identical origin/destination, invalid identifier, arrival before departure, and flight duration above 36 hours return validation errors.
5. Duplicate flight number and scheduled departure in the same trip return 409.
6. Flight list is ordered by scheduled departure and shared to trip members.
7. With no FlightAware key, refresh returns 409 `flight_provider_not_configured` and does not modify the saved flight.
8. With a valid provider key, a matching AeroAPI result updates status and provider timestamp fields.
9. Provider no-match, timeout, HTTP error or malformed response does not overwrite the saved flight status.
10. A scheduled response with a departure estimate or actual departure at least 15 minutes later is labelled delayed; cancellation, diversion, en-route and landed statuses are normalized.
11. A delayed/cancelled/diverted flight reports counts of itinerary activities on departure and arrival UTC dates, but does not claim an exact overlap.
12. More than four refreshes per user per minute are rate-limited.

## Manual UI checks

- Open a trip and add a flight such as UL 503, CMB to LHR.
- Enter departure and arrival using the displayed local date/time controls.
- Confirm the saved route and dates render in the browser's local timezone.
- Confirm a flight without live provider configuration remains "not checked".
- Configure `FLIGHTAWARE_API_KEY` only on the server and test a real flight that falls within AeroAPI's supported data window.
- Confirm provider errors are displayed without changing the previous status.
- Confirm the flight panel stacks correctly on a narrow viewport.

## Product boundaries

The initial implementation is traveller-managed flight details with a manual refresh. It does not ingest airline booking confirmations, check-in status, gates, terminals, baggage, airport operational alerts, connections, ground transfer times, or push notifications. Same-day itinerary review counts are coarse UTC-date signals only. No flight ticket or itinerary is changed.
