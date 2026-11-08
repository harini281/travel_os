-- TravelOS initial PostgreSQL schema (development bootstrap).
-- Docker runs this only when the named database volume is first initialized.

CREATE TABLE IF NOT EXISTS "Users" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "Email" varchar(320) NOT NULL,
  "DisplayName" varchar(120) NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");

CREATE TABLE IF NOT EXISTS "Trips" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "OwnerUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Name" varchar(160) NOT NULL,
  "Destination" varchar(160) NOT NULL,
  "StartDate" date NOT NULL,
  "EndDate" date NOT NULL,
  "CurrencyCode" varchar(3) NOT NULL DEFAULT 'USD',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_Trips_DateRange" CHECK ("EndDate" >= "StartDate"),
  CONSTRAINT "CK_Trips_CurrencyCode" CHECK ("CurrencyCode" ~ '^[A-Z]{3}$')
);

CREATE TABLE IF NOT EXISTS "TripMembers" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Role" varchar(24) NOT NULL DEFAULT 'member',
  "JoinedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TripMembers_TripId_UserId" ON "TripMembers" ("TripId", "UserId");

CREATE TABLE IF NOT EXISTS "Itineraries" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "Version" integer NOT NULL DEFAULT 1,
  "ApprovedByUserId" uuid NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "ApprovedAt" timestamptz NULL,
  CONSTRAINT "CK_Itineraries_Version" CHECK ("Version" > 0)
);

CREATE TABLE IF NOT EXISTS "ItineraryDays" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "ItineraryId" uuid NOT NULL REFERENCES "Itineraries" ("Id") ON DELETE CASCADE,
  "DayNumber" integer NOT NULL,
  "Date" date NOT NULL,
  CONSTRAINT "CK_ItineraryDays_DayNumber" CHECK ("DayNumber" > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItineraryDays_ItineraryId_DayNumber" ON "ItineraryDays" ("ItineraryId", "DayNumber");

CREATE TABLE IF NOT EXISTS "Activities" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "ItineraryDayId" uuid NOT NULL REFERENCES "ItineraryDays" ("Id") ON DELETE CASCADE,
  "Title" varchar(180) NOT NULL,
  "Description" varchar(2000) NULL,
  "PlaceName" varchar(200) NULL,
  "Latitude" numeric(9,6) NULL,
  "Longitude" numeric(9,6) NULL,
  "DurationMinutes" integer NULL,
  "EstimatedCost" numeric(19,4) NULL,
  "CurrencyCode" varchar(3) NULL,
  "SortOrder" integer NOT NULL DEFAULT 0,
  CONSTRAINT "CK_Activities_Latitude" CHECK ("Latitude" IS NULL OR ("Latitude" >= -90 AND "Latitude" <= 90)),
  CONSTRAINT "CK_Activities_Longitude" CHECK ("Longitude" IS NULL OR ("Longitude" >= -180 AND "Longitude" <= 180)),
  CONSTRAINT "CK_Activities_Duration" CHECK ("DurationMinutes" IS NULL OR "DurationMinutes" > 0)
);

CREATE TABLE IF NOT EXISTS "GeneratedItineraryDrafts" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "RequestedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Status" varchar(32) NOT NULL DEFAULT 'draft',
  "ContractVersion" varchar(16) NOT NULL DEFAULT '1.0',
  "PayloadJson" jsonb NOT NULL DEFAULT '{}'::jsonb,
  "CreatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS "Budgets" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL UNIQUE REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "Amount" numeric(19,4) NOT NULL,
  "CurrencyCode" varchar(3) NOT NULL DEFAULT 'USD',
  CONSTRAINT "CK_Budgets_Amount" CHECK ("Amount" >= 0),
  CONSTRAINT "CK_Budgets_CurrencyCode" CHECK ("CurrencyCode" ~ '^[A-Z]{3}$')
);

CREATE TABLE IF NOT EXISTS "BudgetItems" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "BudgetId" uuid NOT NULL REFERENCES "Budgets" ("Id") ON DELETE CASCADE,
  "Category" varchar(80) NOT NULL,
  "Amount" numeric(19,4) NOT NULL,
  "CurrencyCode" varchar(3) NOT NULL DEFAULT 'USD',
  CONSTRAINT "CK_BudgetItems_Amount" CHECK ("Amount" >= 0),
  CONSTRAINT "CK_BudgetItems_CurrencyCode" CHECK ("CurrencyCode" ~ '^[A-Z]{3}$')
);
