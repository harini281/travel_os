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
- Add `AsSplitQuery()` to the itinerary aggregate reads used by opening-hours analysis and route analysis.
- Add `AsSplitQuery()` to the group-poll listing query which loads options and votes.
- Do not set a global `UseQuerySplittingBehavior` default.
- Keep the same authorization checks, projections, tracking behavior, ordering and response DTOs.
- Do not change migrations or schema.

## Trade-off

Split queries issue multiple SELECT statements instead of one joined query. This avoids sibling-collection row multiplication but may observe concurrent data changes between statements unless the surrounding transaction/isolation strategy prevents it. These routes are read-focused; this change deliberately avoids changing application-wide transaction behavior. Validate the loaded graph and endpoint outputs through the existing PostgreSQL integration and browser E2E suites.

## Required validation

- Platform API build and unit tests.
- All PostgreSQL-backed API tests, especially itinerary and group voting endpoints.
- Browser E2E journeys.
- Full deployment smoke and security workflows as regression gates.
- Confirm that the EF Core multiple-collection warning no longer appears in the affected test scenarios.

## Status

**IMPLEMENTATION PUSHED — validation pending.** This branch is separate from the completed .NET 10 migration. Do not call the warning resolved until the exact Stage 53 head passes and the PostgreSQL integration log no longer emits the targeted warning.
