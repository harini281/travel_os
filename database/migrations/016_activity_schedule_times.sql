-- Optional local itinerary activity times; existing activities remain date-only.
ALTER TABLE "Activities"
  ADD COLUMN IF NOT EXISTS "StartTime" time without time zone NULL,
  ADD COLUMN IF NOT EXISTS "EndTime" time without time zone NULL;

ALTER TABLE "Activities"
  ADD CONSTRAINT "CK_Activities_TimeWindow"
  CHECK (
    ("StartTime" IS NULL AND "EndTime" IS NULL)
    OR ("StartTime" IS NOT NULL AND "EndTime" IS NOT NULL AND "EndTime" > "StartTime")
  );
