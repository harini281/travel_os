-- Record the source expense date captured when a conversion was requested.
-- Prevents a conversion snapshot from being reused after an expense date is edited.
ALTER TABLE "TripExpenseConversions"
  ADD COLUMN IF NOT EXISTS "ExpenseDate" date;

-- Backfill older snapshots from their effective rate date. These legacy rows are
-- conservative: if the actual expense date differed, Stage 21 will not use them
-- unless it matches this backfilled date. New snapshots always store exact source date.
UPDATE "TripExpenseConversions" conversion
SET "ExpenseDate" = COALESCE(
    (SELECT expense."ExpenseDate"
     FROM "TripExpenses" expense
     WHERE expense."Id" = conversion."ExpenseId"
       AND expense."TripId" = conversion."TripId"),
    conversion."RateDate")
WHERE "ExpenseDate" IS NULL;

ALTER TABLE "TripExpenseConversions"
  ALTER COLUMN "ExpenseDate" SET NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_TripExpenseConversions_TripId_Target_Expense"
  ON "TripExpenseConversions" ("TripId", "ConvertedCurrencyCode", "ExpenseId", "CreatedAt" DESC);
