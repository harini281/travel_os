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

A member can record a trip expense, its category, amount, currency, date and the member who paid. The payer must be included in the selected split participants, and every participant must be a current trip member. Amounts and shares are stored to four decimal places.

The request accepts `splitType` as `equal` (default), `percentage` or `exact`. Equal splitting divides the total evenly. Percentage splitting requires one positive percentage per participant and a total of exactly 100. Exact splitting requires one positive amount per participant and the rounded amounts must equal the expense total. For percentage rounding, any fractional remainder is assigned deterministically to the first participant by user ID. The API rejects custom shares that do not match the selected participant IDs exactly or that produce a zero/negative share.

Example percentage split:

```json
{
  "title": "Group dinner",
  "category": "food",
  "notes": null,
  "amount": 120,
  "currencyCode": "USD",
  "expenseDate": "2027-07-12",
  "paidByUserId": "00000000-0000-0000-0000-000000000001",
  "participantUserIds": [
    "00000000-0000-0000-0000-000000000001",
    "00000000-0000-0000-0000-000000000002"
  ],
  "splitType": "percentage",
  "customShares": [
    { "userId": "00000000-0000-0000-0000-000000000001", "value": 60 },
    { "userId": "00000000-0000-0000-0000-000000000002", "value": 40 }
  ]
}
```

For exact splitting, `value` is the amount in the expense currency instead of a percentage. Equal splitting does not require `customShares`.

Expense totals and shares retain their original currency. They are not converted or combined across currencies. Settlement and repayment calculations use the saved per-member shares, regardless of the split method used.

## Debt settlement suggestions

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/settlements` | Trip member |

For each currency independently, the API adds the amount paid by each payer and subtracts each member's recorded share to calculate a net balance. Positive balances are owed money; negative balances owe money. A greedy creditor/debtor matching algorithm produces a practical set of suggested transfers. It is a recommendation, not a guarantee of the mathematically smallest possible transfer count.

Currencies are never combined. The endpoint does not initiate payments, mark debts as settled or record repayments. If an expense is deleted, the next calculation reflects the remaining expenses.

## Repayment tracking

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/repayments` | Trip member |
| POST | `/api/v1/trips/{tripId}/repayments` | Debtor who is a trip member |
| POST | `/api/v1/trips/{tripId}/repayments/{repaymentId}/respond` | Intended recipient only |

A debtor can submit a full or partial repayment to a current trip member who has an outstanding positive balance in the same currency. The API checks the current ledger balance and reserves amounts from other pending repayments to reduce accidental over-recording. Amounts are stored to four decimal places.

The repayment starts with status `pending`. Only the intended recipient can confirm or reject it. A confirmed repayment is applied to both users' net balances; rejected and pending repayments do not change the settlement ledger. All records remain visible in the repayment history.

Example create request:

```json
{
  "toUserId": "00000000-0000-0000-0000-000000000000",
  "amount": 25.00,
  "currencyCode": "USD",
  "note": "Bank transfer reference"
}
```

Example response action:

```json
{
  "decision": "confirm"
}
```

Deleting an expense in a currency after a repayment in that currency has been confirmed is blocked. Add a separate adjustment expense instead so the financial history remains auditable. TravelOS records user attestations only; it does not connect to banks, verify transfers, or move money.

## Expense editing and audit history

| Method | Route | Access |
|---|---|---|
| PUT | `/api/v1/trips/{tripId}/expenses/{expenseId}` | Expense creator or trip owner |
| GET | `/api/v1/trips/{tripId}/expenses/{expenseId}/history` | Trip member |

The expense creator or trip owner can update the expense title, category, notes, total, currency, date, payer and participant shares. The update request uses the same validation rules as expense creation. The payer must be one of the selected current trip members. Equal, percentage and exact split modes are supported by the API; the web editor uses exact per-member shares to preserve the existing allocation unless the user changes them.

Every creation, update and deletion appends an audit record with the actor, UTC timestamp, action, and before/after JSON snapshots. Audit rows intentionally do not have a foreign key to the expense row, so deleting an expense does not erase its history. Trip members can view an expense's history while the record exists; deleted records' audit entries remain stored for future administrative/audit tooling.

Edits and deletions are blocked when a confirmed repayment exists in the expense's currency (and edits are also blocked if changing to a currency with confirmed repayments). This is deliberately conservative because repayments are currently recorded at trip/currency ledger level rather than linked to a specific expense. Add an adjustment expense instead. Expense update and its audit row are saved together in one database save operation.

## Receipt management and extraction

| Method | Route | Access |
|---|---|---|
| GET | `/api/v1/trips/{tripId}/receipts` | Trip member |
| POST | `/api/v1/trips/{tripId}/receipts` | Trip member; multipart upload |
| GET | `/api/v1/trips/{tripId}/receipts/{receiptId}/file` | Trip member |
| POST | `/api/v1/trips/{tripId}/receipts/{receiptId}/confirm` | Uploader or trip owner |
| DELETE | `/api/v1/trips/{tripId}/receipts/{receiptId}` | Uploader or trip owner |

Uploads accept JPEG, PNG and WebP images up to 8 MB. The API checks the file signature, generates a random storage name, calculates a SHA-256 hash, and stores bytes outside the public web root. The original filename is retained only as display metadata. Files are private and served only after trip membership is checked. Upload requests are rate-limited per authenticated user.

An identical image hash already present in the same trip is flagged as a possible duplicate; it is not silently discarded. Receipts can be attached to an expense during upload or linked after review. Removing an expense leaves its receipt metadata and image intact, with the expense link set to null.

The platform sends the image to the isolated AI service only when `AiService:BaseUrl` is configured. The AI service defaults to `AI_RECEIPT_PROVIDER=demo`, which does not inspect the image. To opt into OpenAI vision extraction, configure `AI_RECEIPT_PROVIDER=openai` and `OPENAI_API_KEY` in the AI service process. Receipt images then leave the platform and are sent to the configured provider; obtain appropriate user consent and follow your privacy and retention obligations before enabling this in production.

The extracted merchant, date, final total, currency and confidence are suggestions. The API stores them in `needs_review` state, and a user must confirm the corrected values before the receipt becomes `confirmed`. OCR does not create or modify an expense or its financial shares. If extraction is unavailable, the image remains saved in `pending_review` for manual entry.

Set `Receipts:StoragePath` to a private persistent directory. The default is `private-receipts` under the API process directory and is suitable only for local development. Production deployments should use a persistent encrypted volume or private object storage, backups, access logging and a retention/deletion policy. The API currently stores image bytes on local disk; it does not yet integrate object storage or malware scanning.

AI service endpoint: `POST /api/v1/receipts/extract`. The platform validates and stores the receipt independently of the AI result, so extraction failure does not lose the upload.

Apply `database/migrations/009_trip_receipts.sql` after migration 008.
