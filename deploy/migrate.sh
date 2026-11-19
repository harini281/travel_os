#!/bin/sh
set -eu
: "${PGHOST:=postgres}"
: "${PGPORT:=5432}"
: "${PGDATABASE:?PGDATABASE must be set}"
: "${PGUSER:?PGUSER must be set}"
: "${PGPASSWORD:?PGPASSWORD must be set}"
psql -X -v ON_ERROR_STOP=1 <<'SQL'
CREATE TABLE IF NOT EXISTS travelos_migration_history (
    migration_name text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);
SQL
migrations="
001_initial_schema.sql
002_identity_schema.sql
003_itinerary_constraints.sql
003_trip_invitations.sql
004_trip_member_preferences.sql
005_group_polls.sql
006_shared_expenses.sql
007_trip_repayments.sql
008_trip_expense_audits.sql
009_trip_receipts.sql
010_currency_conversions.sql
011_conversion_source_expense_date.sql
012_trip_budget_alerts.sql
013_trip_weather_assessments.sql
014_trip_flights.sql
015_flight_journey_groups.sql
016_activity_schedule_times.sql
017_trip_notifications.sql
"
for migration in $migrations; do
    already_applied="$(psql -X -A -t -v ON_ERROR_STOP=1 -c "SELECT 1 FROM travelos_migration_history WHERE migration_name = '$migration' LIMIT 1")"
    if [ "$already_applied" = "1" ]; then
        echo "Already applied: $migration"
        continue
    fi
    echo "Applying: $migration"
    psql -X -v ON_ERROR_STOP=1 --single-transaction -f "/migrations/$migration" -c "INSERT INTO travelos_migration_history (migration_name) VALUES ('$migration')"
done
echo "TravelOS database migrations are up to date."
