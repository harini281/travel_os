# Activity scheduling — verification plan

## API and database
1. Apply migration 016 after migrations 001–015 on a clean PostgreSQL database.
2. Existing activity rows remain valid with null StartTime and EndTime.
3. POST and PUT accept both times in ISO time format (HH:mm:ss).
4. Supplying only one time returns a validation error.
5. An end time equal to or earlier than the start time returns a validation error.
6. A valid same-day time window is persisted and returned by GET itinerary.
7. Approved itineraries remain read-only.
8. AI-generated activities without times continue to work and remain unscheduled.

## UI
1. Add an activity with no times and confirm it remains date-only.
2. Add a timed activity and confirm its local start/end times render.
3. Add overlapping activities (including nested intervals) and confirm all affected activities are flagged.
4. Change an end time to remove the overlap and reload the itinerary.
5. Verify mobile layout and existing activity duration/cost fields.
6. Confirm no timezone conversion or travel-time estimate is implied.
