-- Add explicit traveller-defined journey groups so unrelated flights are not paired.
ALTER TABLE "TripFlights"
  ADD COLUMN IF NOT EXISTS "JourneyGroup" varchar(80) NOT NULL DEFAULT 'Main journey';

CREATE INDEX IF NOT EXISTS "IX_TripFlights_TripId_JourneyGroup_ScheduledDeparture"
  ON "TripFlights" ("TripId", "JourneyGroup", "ScheduledDepartureUtc");
