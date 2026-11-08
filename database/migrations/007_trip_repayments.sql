-- Repayment records require explicit confirmation by the recipient.
-- Apply after 006_shared_expenses.sql on an existing TravelOS database.

CREATE TABLE IF NOT EXISTS "TripRepayments" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "FromUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "ToUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "CreatedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Amount" numeric(19,4) NOT NULL,
  "CurrencyCode" varchar(3) NOT NULL,
  "Status" varchar(16) NOT NULL DEFAULT 'pending',
  "Note" varchar(500) NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "RespondedAt" timestamptz NULL,
  CONSTRAINT "CK_TripRepayments_Amount" CHECK ("Amount" > 0),
  CONSTRAINT "CK_TripRepayments_Currency" CHECK ("CurrencyCode" ~ '^[A-Z]{3}$'),
  CONSTRAINT "CK_TripRepayments_Status" CHECK ("Status" IN ('pending', 'confirmed', 'rejected')),
  CONSTRAINT "CK_TripRepayments_DifferentUsers" CHECK ("FromUserId" <> "ToUserId")
);

CREATE INDEX IF NOT EXISTS "IX_TripRepayments_TripId_Status_CurrencyCode"
  ON "TripRepayments" ("TripId", "Status", "CurrencyCode");
CREATE INDEX IF NOT EXISTS "IX_TripRepayments_FromUserId_ToUserId_Status"
  ON "TripRepayments" ("FromUserId", "ToUserId", "Status");
