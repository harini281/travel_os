# TravelOS Admin Console — Phase 01

## What is included

- A dedicated premium admin console at `/admin`, separate from the traveller workspace.
- Admin sign-in reuses the existing `POST /api/v1/auth/login` endpoint.
- The console verifies the signed-in identity again through `GET /api/v1/auth/me`.
- Access is shown only when the API returns one of these platform roles: `Admin`, `PlatformAdmin`, or `Administrator`.
- A dedicated admin session key is used in `sessionStorage`; the traveller session key and existing traveller flows are not replaced.
- Responsive navigation for Overview, User management, Trip management, Places & discovery, Analytics, and Settings.
- Honest integration placeholders. The current backend does not yet expose platform-wide admin data endpoints, so the dashboard does not invent counts, users, trips, health states, or analytics.

## Open the console

Start the existing API and web app using the repository's normal local-development instructions, then visit:

`http://localhost:5173/admin`

Use an account that has already been granted a platform administrator role. A normal traveller account must be rejected.

## Grant the first administrator (database operator only)

There is intentionally no public registration flow for administrator accounts. First create a normal account using the existing registration page. Then, as a trusted database operator, run the following SQL after substituting the exact email address of that account.

```sql
BEGIN;

INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
VALUES (gen_random_uuid(), 'Admin', 'ADMIN', gen_random_uuid()::text)
ON CONFLICT ("NormalizedName") DO NOTHING;

INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM "Users" u
JOIN "AspNetRoles" r ON r."NormalizedName" = 'ADMIN'
WHERE lower(u."Email") = lower('replace-with-the-approved-admin-email@example.com')
ON CONFLICT ("UserId", "RoleId") DO NOTHING;

-- Confirm exactly which account received the role before committing.
SELECT u."Email", r."Name"
FROM "Users" u
JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
WHERE lower(u."Email") = lower('replace-with-the-approved-admin-email@example.com');

COMMIT;
```

Only run this using a privileged, audited database connection and an explicitly approved account. Do not expose this SQL through the public application or allow users to choose their own roles. Sign out and sign in again after a role change so a new JWT contains the updated role claim.

## Phase 01.2 API foundation

The API now exposes these read-only endpoints under the `PlatformAdmin` authorization policy:

- `GET /api/v1/admin/dashboard` — total user count, trip count, distinct saved activity place names with coordinates, and generated itinerary draft count.
- `GET /api/v1/admin/users?page=1&pageSize=25&search=...` — paged user directory.
- `GET /api/v1/admin/trips?page=1&pageSize=25&search=...` — paged platform trip directory.

The React overview and user/trip directories consume these endpoints. The directories are intentionally read-only in this phase. Places verification, analytics, settings, account suspension, role editing and trip moderation are not implemented.

## Security boundary

Every `/api/v1/admin` endpoint is protected on the server by the ASP.NET Core `PlatformAdmin` policy, which accepts the `Admin`, `PlatformAdmin`, or `Administrator` role. The client-side role check is only a user-experience gate; the server policy is the actual authorization boundary. Admin endpoints return no data to anonymous users or authenticated travellers.

Role changes are reflected in newly issued JWTs. The current JWT lifetime is 15 minutes, so an already-issued token can retain its previous role claim until it expires. Sign out and sign in again after changing an account's role.

Before adding write operations, implement explicit validation, audit records and safeguards for destructive actions. Do not expose role provisioning or sensitive provider secrets through the public application.

## Validation

Run from `apps/web`:

```bash
npm install
npm run typecheck
npm run build
```

The GitHub connector can commit the source files but does not run the repository's local npm/.NET build, so these commands still need to be run in the development environment.
