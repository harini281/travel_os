#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

usage() {
  echo "Usage: AGE_RECIPIENT=age1... $0 /absolute/path/outside/repository" >&2
  exit 2
}

fail() {
  echo "Backup failed: $1" >&2
  exit 1
}

[[ $# -eq 1 ]] || usage
: "${AGE_RECIPIENT:?Set AGE_RECIPIENT to the approved age encryption recipient.}"

for command_name in docker age sha256sum; do
  command -v "$command_name" >/dev/null 2>&1 || fail "Required command not found: $command_name"
done

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
compose_file="${TRAVELOS_COMPOSE_FILE:-$repo_root/docker-compose.prod.yml}"
env_file="${TRAVELOS_ENV_FILE:-$repo_root/deploy/production.env}"
[[ -r "$compose_file" ]] || fail "Compose file is not readable."
[[ -r "$env_file" ]] || fail "Production environment file is not readable."

output_root="$1"
mkdir -p -- "$output_root"
output_root="$(cd -- "$output_root" && pwd -P)"
case "$output_root/" in
  "$repo_root/"*) fail "Backup output must be outside the repository." ;;
esac

compose=(docker compose --env-file "$env_file" --file "$compose_file")
"${compose[@]}" config --quiet

mapfile -t api_ids < <("${compose[@]}" ps --all --quiet platform-api)
[[ ${#api_ids[@]} -eq 1 ]] || fail "Expected exactly one existing Platform API container to provide the private receipt volume."
api_container_id="${api_ids[0]}"

api_was_running=false
web_was_running=false
api_running_id="$("${compose[@]}" ps --status running --quiet platform-api)"
[[ -n "$api_running_id" ]] || fail "Platform API must be running before capturing a coordinated database/receipt backup."
api_was_running=true
web_running_id="$("${compose[@]}" ps --status running --quiet web)"
if [[ -n "$web_running_id" ]]; then web_was_running=true; fi

backup_name="travelos-backup-$(date -u +%Y%m%dT%H%M%SZ)"
final_dir="$output_root/$backup_name"
[[ ! -e "$final_dir" ]] || fail "Backup destination already exists."

command -v flock >/dev/null 2>&1 || fail "Required command not found: flock"
lock_file="$output_root/.travelos-backup.lock"
exec 9>"$lock_file"
flock -n 9 || fail "Another backup is already running for this destination."
chmod 600 "$lock_file"

work_dir="$(mktemp -d "$output_root/.travelos-backup.XXXXXX")"
chmod 700 "$work_dir"

cleanup() {
  status=$?
  trap - EXIT
  if [[ -n "${work_dir:-}" && -d "$work_dir" ]]; then
    rm -rf -- "$work_dir"
  fi
  if [[ "$api_was_running" == true ]]; then
    "${compose[@]}" start platform-api >/dev/null || {
      echo "WARNING: Platform API could not be restarted automatically." >&2
      status=1
    }
  fi
  if [[ "$api_was_running" == true ]]; then
    api_id="$("${compose[@]}" ps --quiet platform-api)"
    api_healthy=false
    for attempt in $(seq 1 45); do
      if [[ -n "$api_id" ]] && [[ "$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$api_id" 2>/dev/null || true)" == "healthy" ]]; then
        api_healthy=true
        break
      fi
      sleep 2
    done
    if [[ "$api_healthy" != true ]]; then
      echo "WARNING: Platform API did not return to healthy status after backup." >&2
      status=1
    fi
  fi
  if [[ "$web_was_running" == true && "$api_healthy" == true ]]; then
    "${compose[@]}" start web >/dev/null || {
      echo "WARNING: Web service could not be restarted automatically." >&2
      status=1
    }
    web_id="$("${compose[@]}" ps --quiet web)"
    web_healthy=false
    for attempt in $(seq 1 30); do
      if [[ -n "$web_id" ]] && [[ "$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$web_id" 2>/dev/null || true)" == "healthy" ]]; then
        web_healthy=true
        break
      fi
      sleep 2
    done
    if [[ "$web_healthy" != true ]]; then
      echo "WARNING: Web service did not return to healthy status after backup." >&2
      status=1
    fi
  fi
  exit "$status"
}
trap cleanup EXIT

"${compose[@]}" exec -T postgres sh -eu -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' >/dev/null \
  || fail "PostgreSQL is not accepting connections."

# Quiesce application writes while the database and receipt volume are captured.
"${compose[@]}" stop --timeout 60 web platform-api >/dev/null

"${compose[@]}" exec -T postgres sh -eu -c \
  'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump --host=127.0.0.1 --format=custom --no-owner --no-acl --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  > "$work_dir/database.dump"
[[ -s "$work_dir/database.dump" ]] || fail "Database dump is empty."

docker run --rm --network none --volume "$work_dir:/backup:ro" \
  postgres:16.6-bookworm pg_restore --list /backup/database.dump >/dev/null \
  || fail "Database dump failed its PostgreSQL archive validation."

docker run --rm --network none --volumes-from "${api_container_id}:ro" \
  --volume "$work_dir:/backup" alpine:3.20 sh -eu -c \
  'tar -czf /backup/receipts.tar.gz -C /var/lib/travelos/receipts .' \
  || fail "Private receipt volume could not be archived."

docker run --rm --network none --volume "$work_dir:/backup:ro" alpine:3.20 \
  tar -tzf /backup/receipts.tar.gz >/dev/null \
  || fail "Receipt archive failed its integrity listing check."

age --encrypt --recipient "$AGE_RECIPIENT" --output "$work_dir/database.dump.age" "$work_dir/database.dump"
age --encrypt --recipient "$AGE_RECIPIENT" --output "$work_dir/receipts.tar.gz.age" "$work_dir/receipts.tar.gz"
rm -f -- "$work_dir/database.dump" "$work_dir/receipts.tar.gz"

{
  printf 'format=travelos-backup-v1\n'
  printf 'created_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'repository_commit=%s\n' "$(git -C "$repo_root" rev-parse HEAD 2>/dev/null || echo unknown)"
  printf 'database_archive=database.dump.age\n'
  printf 'receipt_archive=receipts.tar.gz.age\n'
  printf 'encryption=age\n'
  printf 'application_writes_paused_during_capture=true\n'
} > "$work_dir/manifest.txt"
(
  cd "$work_dir"
  sha256sum database.dump.age receipts.tar.gz.age manifest.txt > SHA256SUMS
)
chmod 600 "$work_dir"/*
chmod 700 "$work_dir"
mv -- "$work_dir" "$final_dir"
work_dir=""

echo "Encrypted TravelOS database and receipt backup created: $final_dir"
echo "Copy the complete directory to approved off-host, access-controlled backup storage."
