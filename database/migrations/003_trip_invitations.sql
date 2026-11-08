-- Trip invitation table for group collaboration.
-- Apply to an existing TravelOS database after the initial schema and Identity schema.

CREATE TABLE IF NOT EXISTS "TripInvitations" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "InvitedUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "InvitedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Status" varchar(24) NOT NULL DEFAULT 'pending',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "ExpiresAt" timestamptz NOT NULL,
  "RespondedAt" timestamptz NULL,
  CONSTRAINT "CK_TripInvitations_Status"
    CHECK ("Status" IN ('pending', 'accepted', 'revoked', 'expired')),
  CONSTRAINT "CK_TripInvitations_Expiry"
    CHECK ("ExpiresAt" > "CreatedAt")
);

CREATE INDEX IF NOT EXISTS "IX_TripInvitations_TripId_InvitedUserId_Status"
  ON "TripInvitations" ("TripId", "InvitedUserId", "Status");

CREATE INDEX IF NOT EXISTS "IX_TripInvitations_InvitedUserId_Status"
  ON "TripInvitations" ("InvitedUserId", "Status");
