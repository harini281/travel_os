-- Immutable audit trail for shared trip expense changes.
-- Apply after 007_trip_repayments.sql on an existing TravelOS database.
-- ExpenseId intentionally has no foreign key: deletion must not erase audit history.

CREATE TABLE IF NOT EXISTS "TripExpenseAudits" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "ExpenseId" uuid NOT NULL,
  "ChangedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Action" varchar(24) NOT NULL,
  "BeforeJson" jsonb NOT NULL DEFAULT '{}'::jsonb,
  "AfterJson" jsonb NOT NULL DEFAULT '{}'::jsonb,
  "ChangedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_TripExpenseAudits_Action" CHECK ("Action" IN ('created', 'updated', 'deleted'))
);

CREATE INDEX IF NOT EXISTS "IX_TripExpenseAudits_TripId_ExpenseId_ChangedAt"
  ON "TripExpenseAudits" ("TripId", "ExpenseId", "ChangedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_TripExpenseAudits_ChangedByUserId"
  ON "TripExpenseAudits" ("ChangedByUserId");
