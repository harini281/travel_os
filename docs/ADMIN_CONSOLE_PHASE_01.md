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

## Important security boundary

The Phase 01 console is an authenticated UI foundation, not a completed administration API. Hiding a page in React is not authorization. Before enabling user, trip, place, analytics, or settings actions, implement server-side endpoints protected with ASP.NET Core role authorization (for example, `RequireAuthorization(policy => policy.RequireRole("Admin"))`), validate each operation, and record security-relevant changes in an audit log. Do not rely on client-side role checks as the security control.

## Validation

Run from `apps/web`:

```bash
npm install
npm run typecheck
npm run build
```

The GitHub connector can commit the source files but does not run the repository's local npm/.NET build, so these commands still need to be run in the development environment.
