# Stage 36 — time-zone and offline support verification

## Flight time-zone display
1. A UTC flight departure renders in the selected IANA timezone.
2. A UTC flight arrival renders in the selected IANA timezone, including date changes across midnight.
3. Flight status checked-at timestamps use the selected timezone.
4. Changing the selected timezone updates flight timestamps without changing stored UTC values.
5. Each trip retains its own display timezone on this browser.
6. Invalid IANA timezone input is visibly rejected and is not persisted.
7. Activity start/end values remain unchanged local clock times; no timezone conversion is attempted because the API currently stores TimeOnly values without a timezone identifier.
8. The UI explains the difference between UTC flight timestamps and local activity clock times.

## Offline itinerary data
9. Opening a trip online caches its itinerary and trip list/profile in IndexedDB.
10. A traveller can reload the app offline and restore the cached trip list when the session token remains available in the current browser session.
11. Opening a cached trip offline displays its last saved itinerary and its cache timestamp.
12. Offline itinerary mode is read-only; activity edit, delete, add and approval actions are unavailable.
13. Live schedule analysis, flight status, weather, budgets and other API-backed information are not represented as fresh offline data.
14. Returning online and reopening the trip fetches fresh server data and refreshes the local cache.
15. The user can explicitly save or remove an offline itinerary copy.
16. Clearing the offline copy removes the selected trip record from IndexedDB.
17. If IndexedDB is unavailable, online itinerary access still works and the UI reports a graceful storage limitation.
18. Offline data is stored locally without application-level encryption; the UI warns users about shared devices.

## App shell
19. Production builds register the service worker.
20. The service worker caches the app shell, manifest and icon.
21. Same-origin static assets are cached after successful requests.
22. API requests are never cached by the service worker.
23. Third-party OpenStreetMap tile requests are not cached by this service worker.
24. The manifest contains app identity, display mode, theme and icon metadata.
25. App navigation falls back to the cached index document when offline.

## Limitations
- Authentication still depends on a valid token already present in the current session; offline support does not bypass authentication.
- The app does not cache all trip modules, receipts, attachments, weather or flight-provider responses.
- Activity time-zone semantics require a future data-model change (e.g. an IANA zone per trip/day) before reliable destination-local conversion can be applied.
- Browser storage may be evicted by the browser or user; an offline copy is not a guaranteed backup.
