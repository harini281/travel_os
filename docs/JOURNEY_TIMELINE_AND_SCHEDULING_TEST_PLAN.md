# Journey timeline and activity scheduling verification

1. Run migrations 001–016 on a clean PostgreSQL database; confirm existing activities remain valid with null times.
2. Create and read an activity with both local start/end times.
3. Reject one-sided time windows and end times equal to or earlier than start.
4. Update a saved activity through PUT and confirm all fields and times persist.
5. Confirm approved itineraries remain read-only.
6. Confirm unscheduled and AI-generated activities remain date-only.
7. Create overlapping and nested activity windows; verify every affected activity is flagged.
8. Correct a conflict using Edit and confirm the warning clears after reload.
9. Verify the timeline orders timed activities by local time and date-only activities without inventing a time.
10. Confirm overnight flights contribute both departure and arrival dates to same-day manual-review warnings.
11. Verify mobile layout and confirm timeline is read-only.
12. Confirm no timezone conversion, travel-time estimate or booking is implied.
