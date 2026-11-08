# PostgreSQL Development Setup

## Bootstrap schema

The initial schema is in `database/migrations/001_initial_schema.sql`. ASP.NET Core Identity tables and columns are added by `002_identity_schema.sql`.

Docker Compose mounts both scripts into the official PostgreSQL image's initialization directory. They run in filename order only when the PostgreSQL data directory is first initialized.

For an existing development database, take a backup and apply the required migration deliberately. Do not delete a data volume to force initialization if it contains data you need.

## Tables

- Users (ASP.NET Core Identity user records plus TravelOS profile fields)
- AspNetRoles, AspNetRoleClaims
- AspNetUserClaims, AspNetUserLogins, AspNetUserRoles, AspNetUserTokens
- Trips, TripMembers
- Itineraries, ItineraryDays, Activities
- GeneratedItineraryDrafts
- Budgets, BudgetItems

## Data rules

- UUID primary keys.
- UTC timestamps using `timestamptz`.
- Trip dates use PostgreSQL `date`.
- Monetary values use `numeric(19,4)`.
- Currency codes use ISO 4217-style three-letter uppercase codes.
- Normalized email and username indexes support Identity account lookup.
- AI-generated itinerary drafts are separate from approved itinerary records.

## Next database task

Generate and review a versioned EF Core migration from the current model, then replace the bootstrap-only setup with a controlled migration deployment workflow before production use.
