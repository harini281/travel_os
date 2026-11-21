-- Stage 55: durable receipt deletion requests and retry diagnostics.
-- Existing receipt rows remain active; only explicit delete requests are queued.

ALTER TABLE "TripReceipts"
  ADD COLUMN IF NOT EXISTS "DeletionRequestedAt" timestamptz NULL,
  ADD COLUMN IF NOT EXISTS "FileDeletedAt" timestamptz NULL,
  ADD COLUMN IF NOT EXISTS "DeletionAttempts" integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS "LastDeletionError" varchar(500) NULL,
  ADD COLUMN IF NOT EXISTS "NextDeletionAttemptAt" timestamptz NULL;

ALTER TABLE "TripReceipts"
  ADD CONSTRAINT "CK_TripReceipts_DeletionAttempts"
  CHECK ("DeletionAttempts" >= 0);

CREATE INDEX IF NOT EXISTS "IX_TripReceipts_DeletionRequestedAt"
  ON "TripReceipts" ("DeletionRequestedAt")
  WHERE "DeletionRequestedAt" IS NOT NULL;
