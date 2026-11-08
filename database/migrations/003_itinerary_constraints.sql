-- Ensure each trip has at most one itinerary at a given version.
-- Safe to run repeatedly. Existing duplicate (TripId, Version) rows must be
-- reviewed and reconciled before this unique index can be created.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Itineraries_TripId_Version"
  ON "Itineraries" ("TripId", "Version");
