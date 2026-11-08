-- One preference profile per trip member.
-- Apply after 003_trip_invitations.sql on an existing TravelOS database.

CREATE TABLE IF NOT EXISTS "TripMemberPreferences" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
  "InterestsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
  "Pace" varchar(24) NOT NULL DEFAULT 'balanced',
  "TravelStyle" varchar(32) NOT NULL DEFAULT 'mixed',
  "DailyBudgetAmount" numeric(19,4) NULL,
  "DailyBudgetCurrencyCode" varchar(3) NULL,
  "Notes" varchar(500) NULL,
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "UQ_TripMemberPreferences_TripId_UserId" UNIQUE ("TripId", "UserId"),
  CONSTRAINT "CK_TripMemberPreferences_Pace" CHECK ("Pace" IN ('relaxed', 'balanced', 'fast')),
  CONSTRAINT "CK_TripMemberPreferences_TravelStyle" CHECK ("TravelStyle" IN ('mixed', 'culture', 'nature', 'adventure', 'food', 'relaxation')),
  CONSTRAINT "CK_TripMemberPreferences_DailyBudget" CHECK ("DailyBudgetAmount" IS NULL OR "DailyBudgetAmount" >= 0),
  CONSTRAINT "CK_TripMemberPreferences_Currency" CHECK (
    ("DailyBudgetAmount" IS NULL AND "DailyBudgetCurrencyCode" IS NULL) OR
    ("DailyBudgetAmount" IS NOT NULL AND "DailyBudgetCurrencyCode" ~ '^[A-Z]{3}$')
  )
);

CREATE INDEX IF NOT EXISTS "IX_TripMemberPreferences_TripId"
  ON "TripMemberPreferences" ("TripId");
