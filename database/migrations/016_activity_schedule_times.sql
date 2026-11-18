-- Optional local itinerary activity times; existing activities remain date-only.
ALTER TABLE "Activities"
  ADD COLUMN IF NOT EXISTS "StartTime" time without time zone NULL,
  ADD COLUMN IF NOT EXISTS "EndTime" time without time zone NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'CK_Activities_TimeWindow'
  ) THEN
    ALTER TABLE "Activities"
      ADD CONSTRAINT "CK_Activities_TimeWindow"
      CHECK (
        ("StartTime" IS NULL AND "EndTime" IS NULL)
        OR ("StartTime" IS NOT NULL AND "EndTime" IS NOT NULL AND "EndTime" > "StartTime")
      );
  END IF;
END $$;
