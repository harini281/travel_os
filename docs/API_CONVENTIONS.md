# API Conventions

## Base URL

Local platform API: `http://localhost:8080`

All application routes are versioned under `/api/v1`.

## Authentication

Protected routes require:

```http
Authorization: Bearer <access-token>
```

Access tokens are issued by `POST /api/v1/auth/register` and `POST /api/v1/auth/login`.

## Trip management

| Method | Route | Access |
|---|---|---|
| POST | `/api/v1/trips` | Authenticated user |
| GET | `/api/v1/trips` | Authenticated user; own and member trips |
| GET | `/api/v1/trips/{tripId}` | Trip owner or member |
| PUT | `/api/v1/trips/{tripId}` | Trip owner |
| DELETE | `/api/v1/trips/{tripId}` | Trip owner |

### Create trip request

```json
{
  "name": "Summer in Japan",
  "destination": "Tokyo, Japan",
  "startDate": "2027-07-10",
  "endDate": "2027-07-18",
  "currencyCode": "JPY"
}
```

### Rules

- The authenticated user is always the owner; clients cannot supply an owner ID.
- The creator is automatically added as the first trip member with role `owner`.
- Only trip owners can update or delete a trip.
- Members can view trips they belong to.
- Non-members receive `404` for a trip detail request.
- Dates use ISO 8601 calendar-date values.
- Currency codes use three letters and are normalized to uppercase.
- The end date must be on or after the start date.
- The API returns validation errors using the standard problem-details format.

## Error conventions

- `400`: malformed or invalid request.
- `401`: missing or invalid authentication.
- `403`: authenticated user lacks permission for an action.
- `404`: resource does not exist or is not visible to the caller.
- `409`: request conflicts with existing state (for example duplicate account email).
- `429`: authentication rate limit exceeded.
