# PostgreSQL Development Setup

## Bootstrap schema

The initial schema is in `database/migrations/001_initial_schema.sql`. Docker Compose mounts it into the official PostgreSQL image's initialization directory.

The script runs only when the database data directory is first initialized. Existing named volumes are intentionally not modified automatically.

## Tables

- Users
- Trips
- TripMembers
- Itineraries
- ItineraryDays
- Activities
- GeneratedItineraryDrafts
- Budgets
- BudgetItems

## Data rules

- UUID primary keys.
- UTC timestamps using `timestamptz`.
- Trip dates use PostgreSQL `date`.
- Monetary values use `numeric(19,4)`.
- Currency codes use ISO 4217-style three-letter uppercase codes.
- AI-generated itinerary drafts are separate from approved itinerary records.
- Foreign keys enforce ownership references and trip/member relationships.

## Next database task

Generate and review an EF Core migration from the current model, then replace the bootstrap-only setup with a versioned migration deployment workflow before production use.
