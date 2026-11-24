-- Stage 55: durable receipt-file deletion queue.
-- The receipt worker and EF Core entity use these fields to retry failed deletions.
-- Safe to apply to databases that already have any of these columns.

ALTER TABLE "TripReceipts"
    ADD COLUMN IF NOT EXISTS "DeletionRequestedAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "NextDeletionAttemptAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "DeletionAttempts" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "LastDeletionError" varchar(500) NULL;

CREATE INDEX IF NOT EXISTS "IX_TripReceipts_DeletionQueue"
    ON "TripReceipts" ("DeletionRequestedAt", "NextDeletionAttemptAt")
    WHERE "DeletionRequestedAt" IS NOT NULL;
