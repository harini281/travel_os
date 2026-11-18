-- Historical forecast assessments for itinerary activity disruption monitoring.
-- Apply after 012_trip_budget_alerts.sql.

CREATE TABLE IF NOT EXISTS "TripWeatherAssessments" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "ActivityId" uuid NOT NULL REFERENCES "Activities" ("Id") ON DELETE CASCADE,
  "ActivityTitle" varchar(180) NOT NULL,
  "PlaceName" varchar(200),
  "ActivityDate" date NOT NULL,
  "WeatherCode" integer NOT NULL,
  "PrecipitationProbabilityMax" numeric(6,2),
  "PrecipitationSumMm" numeric(8,2),
  "WindSpeedMaxKmh" numeric(8,2),
  "TemperatureMaxC" numeric(6,2),
  "Severity" varchar(16) NOT NULL,
  "Summary" varchar(500) NOT NULL,
  "Recommendation" varchar(500) NOT NULL,
  "Source" varchar(40) NOT NULL DEFAULT 'Open-Meteo',
  "ForecastFetchedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_TripWeatherAssessments_Severity"
    CHECK ("Severity" IN ('low','moderate','high')),
  CONSTRAINT "CK_TripWeatherAssessments_WeatherCode"
    CHECK ("WeatherCode" BETWEEN 0 AND 99),
  CONSTRAINT "CK_TripWeatherAssessments_PrecipitationProbability"
    CHECK ("PrecipitationProbabilityMax" IS NULL OR ("PrecipitationProbabilityMax" >= 0 AND "PrecipitationProbabilityMax" <= 100)),
  CONSTRAINT "CK_TripWeatherAssessments_NonNegativeMetrics"
    CHECK (
      ("PrecipitationSumMm" IS NULL OR "PrecipitationSumMm" >= 0)
      AND ("WindSpeedMaxKmh" IS NULL OR "WindSpeedMaxKmh" >= 0)
    )
);
CREATE INDEX IF NOT EXISTS "IX_TripWeatherAssessments_TripId_FetchedAt"
  ON "TripWeatherAssessments" ("TripId","ForecastFetchedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_TripWeatherAssessments_ActivityId_Date_FetchedAt"
  ON "TripWeatherAssessments" ("ActivityId","ActivityDate","ForecastFetchedAt" DESC);
