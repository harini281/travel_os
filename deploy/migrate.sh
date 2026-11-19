#!/bin/sh
set -eu
: "${PGHOST:=postgres}"
: "${PGPORT:=5432}"
: "${PGDATABASE:?PGDATABASE must be set}"
: "${PGUSER:?PGUSER must be set}"
: "${PGPASSWORD:?PGPASSWORD must be set}"
: "${TRAVELOS_APP_DB_USER:?TRAVELOS_APP_DB_USER must be set}"
: "${TRAVELOS_APP_DB_PASSWORD:?TRAVELOS_APP_DB_PASSWORD must be set}"

case "$TRAVELOS_APP_DB_USER" in
    ''|*[!A-Za-z0-9_]*)
        echo "TRAVELOS_APP_DB_USER must contain only letters, numbers and underscores." >&2
        exit 1
        ;;
esac
[ "${#TRAVELOS_APP_DB_USER}" -le 63 ] || {
    echo "TRAVELOS_APP_DB_USER must be at most 63 characters." >&2
    exit 1
}
[ "$TRAVELOS_APP_DB_USER" != "$PGUSER" ] || {
    echo "TRAVELOS_APP_DB_USER must be separate from the PostgreSQL migration administrator." >&2
    exit 1
}
[ "$TRAVELOS_APP_DB_PASSWORD" != "$PGPASSWORD" ] || {
    echo "The application database password must be different from the migration administrator password." >&2
    exit 1
}

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

# Keep the runtime API account separate from the bootstrap/migration administrator.
# psql reads its values from the service environment; credentials are not command arguments.
psql -X -v ON_ERROR_STOP=1 <<'SQL'
\getenv app_user TRAVELOS_APP_DB_USER
\getenv app_password TRAVELOS_APP_DB_PASSWORD

SELECT format(
    'CREATE ROLE %I WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
    :'app_user', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user');
\gexec

-- Remove any previously granted role memberships before normalizing privileges.
SELECT format('REVOKE %I FROM %I', granted_role.rolname, member_role.rolname)
FROM pg_auth_members memberships
JOIN pg_roles granted_role ON granted_role.oid = memberships.roleid
JOIN pg_roles member_role ON member_role.oid = memberships.member
WHERE member_role.rolname = :'app_user';
\gexec

SELECT format(
    'ALTER ROLE %I WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
    :'app_user', :'app_password');
\gexec

SELECT format('REVOKE CREATE, TEMPORARY ON DATABASE %I FROM %I', current_database(), :'app_user');
\gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'app_user');
\gexec
REVOKE CREATE ON SCHEMA public FROM :"app_user";
GRANT USAGE ON SCHEMA public TO :"app_user";
REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM :"app_user";
REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public FROM :"app_user";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_user";
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO :"app_user";
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON TABLE travelos_migration_history FROM :"app_user";
GRANT SELECT ON TABLE travelos_migration_history TO :"app_user";

SELECT format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
    current_user, :'app_user');
\gexec
SELECT format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I',
    current_user, :'app_user');
\gexec
SQL

echo "TravelOS database migrations and application-role grants are up to date."
