-- Private trip receipt metadata and reviewable OCR extraction.
-- Receipt image bytes are stored outside the web root in private persistent storage.
-- Apply after database/migrations/008_trip_expense_audits.sql.

CREATE TABLE IF NOT EXISTS "TripReceipts" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "ExpenseId" uuid NULL REFERENCES "TripExpenses" ("Id") ON DELETE SET NULL,
  "UploadedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "OriginalFileName" varchar(255) NOT NULL,
  "StoredFileName" varchar(255) NOT NULL,
  "ContentType" varchar(80) NOT NULL,
  "FileSizeBytes" bigint NOT NULL,
  "Sha256" varchar(64) NOT NULL,
  "ExtractionStatus" varchar(24) NOT NULL DEFAULT 'pending_review',
  "MerchantName" varchar(180) NULL,
  "ReceiptDate" date NULL,
  "TotalAmount" numeric(19,4) NULL,
  "CurrencyCode" varchar(3) NULL,
  "Confidence" numeric(5,4) NULL,
  "ExtractionWarning" varchar(500) NULL,
  "UploadedAt" timestamptz NOT NULL DEFAULT now(),
  "ConfirmedAt" timestamptz NULL,
  "ConfirmedByUserId" uuid NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  CONSTRAINT "CK_TripReceipts_FileSize" CHECK ("FileSizeBytes" > 0 AND "FileSizeBytes" <= 8388608),
  CONSTRAINT "CK_TripReceipts_ExtractionStatus" CHECK ("ExtractionStatus" IN ('pending_review', 'needs_review', 'confirmed')),
  CONSTRAINT "CK_TripReceipts_Amount" CHECK ("TotalAmount" IS NULL OR "TotalAmount" >= 0),
  CONSTRAINT "CK_TripReceipts_Confidence" CHECK ("Confidence" IS NULL OR ("Confidence" >= 0 AND "Confidence" <= 1))
);

CREATE INDEX IF NOT EXISTS "IX_TripReceipts_TripId_Sha256"
  ON "TripReceipts" ("TripId", "Sha256");
CREATE INDEX IF NOT EXISTS "IX_TripReceipts_TripId_ExpenseId_UploadedAt"
  ON "TripReceipts" ("TripId", "ExpenseId", "UploadedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_TripReceipts_UploadedByUserId"
  ON "TripReceipts" ("UploadedByUserId");
