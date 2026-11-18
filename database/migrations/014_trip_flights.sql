-- Traveller-managed flight details and latest provider status snapshot.
-- Apply after 013_trip_weather_assessments.sql.

CREATE TABLE IF NOT EXISTS "TripFlights" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "CreatedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "FlightIdentifier" varchar(24) NOT NULL,
  "OriginAirportCode" varchar(3) NOT NULL,
  "DestinationAirportCode" varchar(3) NOT NULL,
  "ScheduledDepartureUtc" timestamptz NOT NULL,
  "ScheduledArrivalUtc" timestamptz NOT NULL,
  "Status" varchar(24) NOT NULL DEFAULT 'not_checked',
  "Provider" varchar(32) NOT NULL DEFAULT 'none',
  "ProviderFlightId" varchar(80),
  "ProviderStatus" varchar(120),
  "EstimatedDepartureUtc" timestamptz,
  "ActualDepartureUtc" timestamptz,
  "EstimatedArrivalUtc" timestamptz,
  "ActualArrivalUtc" timestamptz,
  "DepartureDelayMinutes" integer,
  "LastCheckedAt" timestamptz,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_TripFlights_Status" CHECK ("Status" IN ('not_checked','scheduled','delayed','active','landed','cancelled','diverted','unknown')),
  CONSTRAINT "CK_TripFlights_Airports" CHECK ("OriginAirportCode" ~ '^[A-Z]{3}$' AND "DestinationAirportCode" ~ '^[A-Z]{3}$' AND "OriginAirportCode" <> "DestinationAirportCode"),
  CONSTRAINT "CK_TripFlights_Schedule" CHECK ("ScheduledArrivalUtc" > "ScheduledDepartureUtc"),
  CONSTRAINT "CK_TripFlights_Delay" CHECK ("DepartureDelayMinutes" IS NULL OR "DepartureDelayMinutes" BETWEEN -1440 AND 10080)
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TripFlights_TripId_Identifier_Departure"
  ON "TripFlights" ("TripId","FlightIdentifier","ScheduledDepartureUtc");
CREATE INDEX IF NOT EXISTS "IX_TripFlights_TripId_ScheduledDeparture"
  ON "TripFlights" ("TripId","ScheduledDepartureUtc");
