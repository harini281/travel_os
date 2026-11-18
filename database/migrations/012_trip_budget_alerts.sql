-- Shared trip budget alert history with per-member read state.
-- Apply after 011_conversion_source_expense_date.sql.

CREATE TABLE IF NOT EXISTS "TripBudgetAlerts" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "BudgetId" uuid NOT NULL REFERENCES "Budgets" ("Id") ON DELETE CASCADE,
  "AlertType" varchar(32) NOT NULL,
  "ThresholdPercent" integer NOT NULL DEFAULT 0,
  "BudgetAmountSnapshot" numeric(19,4) NOT NULL,
  "CurrencyCode" varchar(3) NOT NULL,
  "ActualAmountSnapshot" numeric(19,4) NOT NULL,
  "ProjectedAmountSnapshot" numeric(19,4),
  "Message" varchar(500) NOT NULL,
  "TriggeredAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_TripBudgetAlerts_Type" CHECK ("AlertType" IN ('threshold','projected_over_budget')),
  CONSTRAINT "CK_TripBudgetAlerts_Threshold" CHECK ("ThresholdPercent" IN (0,50,75,90,100)),
  CONSTRAINT "CK_TripBudgetAlerts_Amounts" CHECK (
    "BudgetAmountSnapshot" >= 0 AND "ActualAmountSnapshot" >= 0
    AND ("ProjectedAmountSnapshot" IS NULL OR "ProjectedAmountSnapshot" >= 0)
  ),
  CONSTRAINT "CK_TripBudgetAlerts_Currency" CHECK ("CurrencyCode" ~ '^[A-Z]{3}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TripBudgetAlerts_Dedupe"
  ON "TripBudgetAlerts" ("TripId","BudgetId","AlertType","ThresholdPercent","BudgetAmountSnapshot","CurrencyCode");
CREATE INDEX IF NOT EXISTS "IX_TripBudgetAlerts_TripId_TriggeredAt"
  ON "TripBudgetAlerts" ("TripId","TriggeredAt" DESC);

CREATE TABLE IF NOT EXISTS "TripBudgetAlertReads" (
  "AlertId" uuid NOT NULL REFERENCES "TripBudgetAlerts" ("Id") ON DELETE CASCADE,
  "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
  "ReadAt" timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY ("AlertId","UserId")
);
CREATE INDEX IF NOT EXISTS "IX_TripBudgetAlertReads_UserId"
  ON "TripBudgetAlertReads" ("UserId");
