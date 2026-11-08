-- Shared trip expenses and equal participant shares.
-- Apply after 005_group_polls.sql on an existing TravelOS database.

CREATE TABLE IF NOT EXISTS "TripExpenses" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "PaidByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "CreatedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Title" varchar(160) NOT NULL,
  "Category" varchar(40) NOT NULL,
  "Notes" varchar(500) NULL,
  "Amount" numeric(19,4) NOT NULL,
  "CurrencyCode" varchar(3) NOT NULL,
  "ExpenseDate" date NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_TripExpenses_Amount" CHECK ("Amount" > 0),
  CONSTRAINT "CK_TripExpenses_Currency" CHECK ("CurrencyCode" ~ '^[A-Z]{3}$')
);

CREATE TABLE IF NOT EXISTS "TripExpenseShares" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "ExpenseId" uuid NOT NULL REFERENCES "TripExpenses" ("Id") ON DELETE CASCADE,
  "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "ShareAmount" numeric(19,4) NOT NULL,
  CONSTRAINT "CK_TripExpenseShares_Amount" CHECK ("ShareAmount" >= 0),
  CONSTRAINT "UQ_TripExpenseShares_ExpenseId_UserId" UNIQUE ("ExpenseId", "UserId")
);

CREATE INDEX IF NOT EXISTS "IX_TripExpenses_TripId_ExpenseDate"
  ON "TripExpenses" ("TripId", "ExpenseDate" DESC);
