-- Stage 37: persistent in-app notifications and scheduled reminders.
CREATE TABLE IF NOT EXISTS "TripNotifications" (
    "Id" uuid PRIMARY KEY,
    "TripId" uuid NOT NULL REFERENCES "Trips"("Id") ON DELETE CASCADE,
    "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "CreatedByUserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
    "Type" varchar(32) NOT NULL,
    "Title" varchar(120) NOT NULL,
    "Message" varchar(500) NOT NULL,
    "ScheduledForUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "ReadAt" timestamp with time zone NULL,
    "DismissedAt" timestamp with time zone NULL,
    "DedupeKey" varchar(240) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TripNotifications_DedupeKey"
    ON "TripNotifications" ("DedupeKey");
CREATE INDEX IF NOT EXISTS "IX_TripNotifications_UserId_ScheduledForUtc_DismissedAt"
    ON "TripNotifications" ("UserId", "ScheduledForUtc", "DismissedAt");
CREATE INDEX IF NOT EXISTS "IX_TripNotifications_TripId_CreatedAt"
    ON "TripNotifications" ("TripId", "CreatedAt");
