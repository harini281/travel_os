-- Immutable historical currency conversion snapshots for trip expenses.
-- Apply after 009_trip_receipts.sql on an existing TravelOS database.
-- ExpenseId intentionally has no FK so snapshots remain available for audit after expense removal.

CREATE TABLE IF NOT EXISTS "TripExpenseConversions" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "ExpenseId" uuid NOT NULL,
  "RequestedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "OriginalAmount" numeric(19,4) NOT NULL,
  "OriginalCurrencyCode" varchar(3) NOT NULL,
  "ConvertedAmount" numeric(19,4) NOT NULL,
  "ConvertedCurrencyCode" varchar(3) NOT NULL,
  "ExchangeRate" numeric(28,12) NOT NULL,
  "RateDate" date NOT NULL,
  "Provider" varchar(120) NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_TripExpenseConversions_OriginalAmount" CHECK ("OriginalAmount" >= 0),
  CONSTRAINT "CK_TripExpenseConversions_ConvertedAmount" CHECK ("ConvertedAmount" >= 0),
  CONSTRAINT "CK_TripExpenseConversions_ExchangeRate" CHECK ("ExchangeRate" > 0),
  CONSTRAINT "CK_TripExpenseConversions_Currency" CHECK (
    "OriginalCurrencyCode" ~ '^[A-Z]{3}$' AND "ConvertedCurrencyCode" ~ '^[A-Z]{3}$'
  )
);

CREATE INDEX IF NOT EXISTS "IX_TripExpenseConversions_TripId_ExpenseId_CreatedAt"
  ON "TripExpenseConversions" ("TripId", "ExpenseId", "CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_TripExpenseConversions_RequestedByUserId"
  ON "TripExpenseConversions" ("RequestedByUserId");
