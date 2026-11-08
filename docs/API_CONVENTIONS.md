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

## Traveller preferences

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/preferences/me` | Trip member; own profile only |
| PUT | `/api/v1/trips/{tripId}/preferences/me` | Trip member; own profile only |
| GET | `/api/v1/trips/{tripId}/preferences/group` | Trip member; shared summaries |

A member can choose up to eight interests, a preferred pace (`relaxed`, `balanced`, `fast`), a travel style (`mixed`, `culture`, `nature`, `adventure`, `food`, `relaxation`) and an optional daily budget with ISO-style three-letter currency code. Optional personal notes are limited to 500 characters.

The group endpoint returns shared interests, pace, travel style and optional daily budget. It deliberately does not return personal notes. Budgets remain in each traveller's stated currency; no currency conversion is applied. Preference data is user-submitted context and does not automatically alter or approve an itinerary.

## Group voting

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/polls` | Trip member |
| POST | `/api/v1/trips/{tripId}/polls` | Trip member |
| POST | `/api/v1/trips/{tripId}/polls/{pollId}/vote` | Trip member; open polls only |
| POST | `/api/v1/trips/{tripId}/polls/{pollId}/close` | Poll creator or trip owner |

Polls have a title, optional description, 2–8 distinct options and an optional closing time. Each member has one vote per poll and can change that vote while the poll is open. Results show option totals and percentages. Polls close when the creator or trip owner closes them, or when their closing time has passed and the poll list is next loaded or a vote is attempted.

A poll is a group decision aid, not an automatic itinerary change. The group still needs to apply the chosen result to the trip plan.

## Shared expenses

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/expenses` | Trip member |
| POST | `/api/v1/trips/{tripId}/expenses` | Trip member |
| DELETE | `/api/v1/trips/{tripId}/expenses/{expenseId}` | Expense creator or trip owner |

A member can record a trip expense, its category, amount, currency, date and the member who paid. The payer must be included in the selected split participants, and every participant must be a current trip member. The API calculates an equal share for each selected participant. Amounts are stored to four decimal places; any fractional remainder is assigned deterministically to the first participant by user ID so that shares add up exactly to the recorded amount.

Expense totals and shares retain their original currency. They are not converted or combined across currencies. This stage records expense shares only; it does not yet calculate net trip balances, reimbursements, settlements, receipts or custom split ratios.

## Debt settlement suggestions

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/settlements` | Trip member |

For each currency independently, the API adds the amount paid by each payer and subtracts each member's recorded share to calculate a net balance. Positive balances are owed money; negative balances owe money. A greedy creditor/debtor matching algorithm produces a practical set of suggested transfers. It is a recommendation, not a guarantee of the mathematically smallest possible transfer count.

Currencies are never combined. The endpoint does not initiate payments, mark debts as settled or record repayments. If an expense is deleted, the next calculation reflects the remaining expenses.
