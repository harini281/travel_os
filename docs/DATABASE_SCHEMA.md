# Initial Database Schema

Stage 1 defines the domain model and relationship plan. The first EF Core migration should be generated after the model is reviewed and the .NET SDK is available.

## Identity
- Users
- UserProfiles
- RefreshTokens (future authentication hardening)

## Destination catalogue
- Countries
- Destinations
- Places

## Trip management
- Trips
- TripMembers
- TripPreferences
- Invitations

## Itinerary
- Itineraries (approved/current itinerary aggregate)
- ItineraryDays
- Activities
- GeneratedItineraryDrafts (separate, unapproved AI output)

## Budget and expenses
- Budgets
- BudgetItems
- Expenses
- ExpenseSplits
- Settlements
- Currencies

## AI and audit
- AIRequests
- AuditLogs

## Core relationships

```text
User 1---* Trips (owner)
User *---* Trips through TripMembers
Trip 1---* TripPreferences
Trip 1---* Invitations
Trip 1---* GeneratedItineraryDrafts
Trip 1---* Itineraries
Itinerary 1---* ItineraryDays
ItineraryDay 1---* Activities
Trip 1---0..1 Budget
Budget 1---* BudgetItems
Trip 1---* Expenses
Expense 1---* ExpenseSplits
```

## Data rules

- Use UUID primary keys.
- Store instants in UTC (`timestamp with time zone`).
- Store currency using ISO 4217 codes.
- Store money as `numeric(19,4)`, never floating point.
- Keep original amount/currency and converted estimate/rate timestamp separately.
- Use restrictive foreign keys for financial and approved itinerary records.
