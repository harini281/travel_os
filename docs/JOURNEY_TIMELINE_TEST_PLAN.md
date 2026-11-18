# Smart journey timeline — verification plan

1. Open a trip with no flights and no activities; verify the empty state.
2. Add a flight; verify it appears with departure, arrival and current status.
3. Add itinerary activities on trip dates; verify each appears as a date-only plan.
4. Add multiple flights; verify chronological ordering.
5. Verify provider actual times take precedence over estimated and scheduled times when present.
6. Verify same-day flight/activity notice appears, and is described as a manual review prompt rather than a confirmed conflict.
7. Verify activities do not display fabricated start times.
8. Verify switching trips clears and reloads the relevant timeline data.
9. Verify the timeline is readable on mobile and desktop.
10. Confirm timeline rendering is read-only and does not mutate bookings or itinerary data.
