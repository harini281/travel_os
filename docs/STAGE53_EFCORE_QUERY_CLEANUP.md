# Stage 53 — EF Core split-query cleanup

**Branch:** `stage53-efcore-query-cleanup`  
**Base:** `stage52-dotnet10-migration`  
**Goal:** Resolve EF Core's multiple-collection query warning without changing global query behavior or API response contracts.

## Findings from the .NET 10 test run

The PostgreSQL integration suite passed but emitted EF Core warning `RelationalEventId.MultipleCollectionIncludeWarning` on read paths which load more than one collection:
- Itinerary aggregate reads load `Itinerary.Days` and nested `ItineraryDay.Activities`.
- Group poll listing loads `GroupPoll.Options` and `GroupPoll.Votes`.

Those patterns can produce large joined result sets (cartesian row multiplication). The warning is not a test failure, but leaving query splitting implicit makes the SQL shape unclear and can increase database/network work as trips and polls grow.

## Changes

- Add `AsSplitQuery()` to the three itinerary aggregate reads in `ItineraryEndpoints` (schedule analysis, itinerary retrieval and approval).
- Add `AsSplitQuery()` to the two itinerary aggregate reads used by recommendation scheduling/generation.
- Add `AsSplitQuery()` to the itinerary aggregate reads used by opening-hours analysis, route analysis and the trip-assistant context projection.
- Add `AsSplitQuery()` to the group-poll listing query which loads options and votes.
- Do not set a global `UseQuerySplittingBehavior` default.
- In the `Testing` environment only, turn `MultipleCollectionIncludeWarning` into an exception to prevent this warning from silently returning in future PostgreSQL integration tests.
- Keep the same authorization checks, projections, tracking behavior, ordering and response DTOs.
- Do not change migrations or schema.

## Trade-off

Split queries issue multiple SELECT statements instead of one joined query. This avoids sibling-collection row multiplication but may observe concurrent data changes between statements unless the surrounding transaction/isolation strategy prevents it. These routes are read-focused; this change deliberately avoids changing application-wide transaction behavior. Validate the loaded graph and endpoint outputs through the existing PostgreSQL integration and browser E2E suites.

## Validation result — PASS on implementation commit `9b36cd9346ec57f448373321fd7169bd00f8ec45`

[TravelOS CI run 44681040370](https://github.com/harini281/travel_os/actions/runs/44681040370) — **PASS, all 8 jobs**:
- Platform API build — passed.
- Platform API unit tests — **49 passed, 0 failed, 27 skipped** (PostgreSQL-only tests are covered by the separate integration job).
- PostgreSQL-backed API tests — **80 passed, 0 failed**. The log contained **zero** `MultipleCollectionIncludeWarning` messages while the test-only warning-to-exception guard was enabled.
- PostgreSQL migrations — passed.
- AI API tests — **48 passed**.
- Web typecheck/build — passed.
- Docker deployment smoke — passed: all application images built, services started and reported healthy, migration history was complete, and internal API/database network isolation checks passed.
- Browser E2E — **16 passed, 0 failed** across Chromium, Firefox, WebKit, mobile Chromium, mobile WebKit and tablet Chromium. Receipt and accessibility journeys passed.

[TravelOS Security Assessment run 44681040319](https://github.com/harini281/travel_os/actions/runs/44681040319) — **PASS, all 5 jobs**:
- CodeQL Platform API — passed; SARIF uploaded to GitHub Code Scanning.
- CodeQL Web and AI — passed; SARIF uploaded to GitHub Code Scanning.
- NuGet/npm/Python dependency audit — passed.
- Filesystem/secret/config scan — passed.
- Trivy `v0.76.2` — Platform API, AI API and web images all passed the HIGH/CRITICAL gate; all three base-image summaries reported 0 findings at that threshold.

## Trade-off

Split queries issue multiple SELECT statements instead of one joined query. This avoids sibling-collection row multiplication but may observe concurrent data changes between statements unless the surrounding transaction/isolation strategy prevents it. These routes are read-focused; this change deliberately avoids changing application-wide transaction behavior.

## Status

**EF CORE QUERY CLEANUP: VERIFIED.** The exact implementation commit passed all 8 CI jobs and all 5 security jobs. The PostgreSQL integration suite passed 80 tests and emitted zero multiple-collection warnings with the strict test guard active. This is repository-level verification, not independent penetration testing or production approval; retain the separate operational and staging release gates.

