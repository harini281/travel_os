-- Group polls with a single replaceable vote per member.
-- Apply after 004_trip_member_preferences.sql on an existing TravelOS database.

CREATE TABLE IF NOT EXISTS "GroupPolls" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TripId" uuid NOT NULL REFERENCES "Trips" ("Id") ON DELETE CASCADE,
  "CreatedByUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "Title" varchar(160) NOT NULL,
  "Description" varchar(1000) NULL,
  "Status" varchar(16) NOT NULL DEFAULT 'open',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "ClosesAt" timestamptz NULL,
  CONSTRAINT "CK_GroupPolls_Status" CHECK ("Status" IN ('open', 'closed'))
);

CREATE TABLE IF NOT EXISTS "GroupPollOptions" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "PollId" uuid NOT NULL REFERENCES "GroupPolls" ("Id") ON DELETE CASCADE,
  "Label" varchar(160) NOT NULL,
  "SortOrder" integer NOT NULL,
  CONSTRAINT "UQ_GroupPollOptions_PollId_SortOrder" UNIQUE ("PollId", "SortOrder")
);

CREATE TABLE IF NOT EXISTS "GroupPollVotes" (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "PollId" uuid NOT NULL REFERENCES "GroupPolls" ("Id") ON DELETE CASCADE,
  "OptionId" uuid NOT NULL REFERENCES "GroupPollOptions" ("Id") ON DELETE CASCADE,
  "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "UQ_GroupPollVotes_PollId_UserId" UNIQUE ("PollId", "UserId")
);

CREATE INDEX IF NOT EXISTS "IX_GroupPolls_TripId_CreatedAt"
  ON "GroupPolls" ("TripId", "CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_GroupPollVotes_OptionId"
  ON "GroupPollVotes" ("OptionId");
