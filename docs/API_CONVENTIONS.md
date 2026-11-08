# API Conventions

## Base URL

Local platform API: `http://localhost:8080`

All application routes are versioned under `/api/v1`. Protected routes require a bearer access token.

## Authentication

```http
Authorization: Bearer <access-token>
```

## Trip management

| Method | Route | Access |
|---|---|---|
| POST | `/api/v1/trips` | Authenticated user |
| GET | `/api/v1/trips` | Authenticated user; own and member trips |
| GET | `/api/v1/trips/{tripId}` | Trip owner or member |
| PUT | `/api/v1/trips/{tripId}` | Trip owner |
| DELETE | `/api/v1/trips/{tripId}` | Trip owner |

## Itinerary management

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/itinerary` | Trip owner or member |
| POST | `/api/v1/trips/{tripId}/itinerary` | Trip owner |
| POST | `/api/v1/trips/{tripId}/itinerary/days` | Trip owner |
| POST | `/api/v1/trips/{tripId}/itinerary/days/{dayId}/activities` | Trip owner |
| PUT | `/api/v1/trips/{tripId}/itinerary/activities/{activityId}` | Trip owner |
| DELETE | `/api/v1/trips/{tripId}/itinerary/activities/{activityId}` | Trip owner |
| POST | `/api/v1/trips/{tripId}/itinerary/approve` | Trip owner |

### Add day

```json
{
  "dayNumber": 1,
  "date": "2027-07-10"
}
```

The day number must match the date's one-based position in the trip date range. A date can only be added once.

### Add activity

```json
{
  "title": "Visit Fushimi Inari Shrine",
  "description": "Walk the lower torii gate trail.",
  "placeName": "Fushimi Inari Taisha",
  "latitude": 34.9671,
  "longitude": 135.7727,
  "durationMinutes": 120,
  "estimatedCost": 0,
  "currencyCode": "JPY",
  "sortOrder": 1
}
```

Coordinates and duration are optional. If cost is supplied without a currency, the trip's currency is used. Cost is an estimate and does not represent live pricing.

### Approval rules

- The owner creates the initial itinerary and manages days and activities.
- Members can view the itinerary but cannot edit it.
- Approval requires at least one day containing at least one activity.
- Once approved, the itinerary is read-only.
- Editing an approved itinerary through a new version is a planned follow-up, not implemented yet.

## Error conventions

- `400`: malformed or invalid request.
- `401`: missing or invalid authentication.
- `403`: authenticated user lacks permission for an action.
- `404`: resource does not exist or is not visible to the caller.
- `409`: request conflicts with existing state.
- `429`: authentication rate limit exceeded.

## Itinerary management

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/itinerary` | Trip owner or member |
| POST | `/api/v1/trips/{tripId}/itinerary` | Trip owner |
| POST | `/api/v1/trips/{tripId}/itinerary/days` | Trip owner |
| POST | `/api/v1/trips/{tripId}/itinerary/days/{dayId}/activities` | Trip owner |
| PUT | `/api/v1/trips/{tripId}/itinerary/activities/{activityId}` | Trip owner |
| DELETE | `/api/v1/trips/{tripId}/itinerary/activities/{activityId}` | Trip owner |
| POST | `/api/v1/trips/{tripId}/itinerary/approve` | Trip owner |

The web app supports initial itinerary creation, adding days and activities, activity removal, and approval. Approved itineraries are read-only.

## AI itinerary drafts

| Method | Route | Access |
|---|---|---|
| POST | `/api/v1/trips/{tripId}/itinerary/drafts` | Trip owner; requests generation |
| GET | `/api/v1/trips/{tripId}/itinerary/drafts` | Trip owner or member; lists drafts |
| GET | `/api/v1/trips/{tripId}/itinerary/drafts/{draftId}` | Trip owner or member |
| POST | `/api/v1/trips/{tripId}/itinerary/drafts/{draftId}/apply` | Trip owner; applies reviewed draft |

The generation request accepts `travellerCount`, optional `budgetAmount`, `interests` and `travelPace`. Destination, dates and currency are taken from the stored trip record.

The API persists only a successful real-provider result. Demo-mode responses are not saved as generated itineraries. Applying a draft creates a new itinerary version and never approves it automatically.

## Budget management

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/budget` | Trip owner or member |
| PUT | `/api/v1/trips/{tripId}/budget` | Trip owner |
| POST | `/api/v1/trips/{tripId}/budget/items` | Trip owner |
| PUT | `/api/v1/trips/{tripId}/budget/items/{itemId}` | Trip owner |
| DELETE | `/api/v1/trips/{tripId}/budget/items/{itemId}` | Trip owner |

### Upsert budget

```json
{
  "amount": 1500,
  "currencyCode": "USD"
}
```

### Add planned item

```json
{
  "category": "Accommodation",
  "amount": 500,
  "currencyCode": "USD"
}
```

When an item currency is omitted, the budget currency is used. Item totals are grouped by currency; the API does not combine currencies without a conversion rate.

## Group collaboration

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/members` | Trip owner or member |
| POST | `/api/v1/trips/{tripId}/invitations` | Trip owner |
| GET | `/api/v1/trips/{tripId}/invitations` | Trip owner |
| DELETE | `/api/v1/trips/{tripId}/invitations/{invitationId}` | Trip owner; pending only |
| POST | `/api/v1/trips/{tripId}/invitations/{invitationId}/accept` | Invited account only |
| DELETE | `/api/v1/trips/{tripId}/members/{memberUserId}` | Trip owner; cannot remove owner |
| GET | `/api/v1/me/invitations` | Authenticated invited user |

Invitations are currently limited to existing TravelOS accounts. The owner supplies the account email; the invited account sees pending invitations in its TravelOS inbox and must be signed in to accept. Invitations expire after seven days. Email delivery, public invitation links and invitations to unregistered email addresses are not implemented.

Only the trip owner can invite or remove members. Members can view the trip, itinerary, budget and group roster. Accepting an invitation creates a member role; it does not grant owner permissions.
