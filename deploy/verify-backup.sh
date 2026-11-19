#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

usage() {
  echo "Usage: AGE_IDENTITY_FILE=/secure/path/identity.txt $0 /path/to/travelos-backup-directory" >&2
  exit 2
}
fail() {
  echo "Backup verification failed: $1" >&2
  exit 1
}

[[ $# -eq 1 ]] || usage
: "${AGE_IDENTITY_FILE:?Set AGE_IDENTITY_FILE to the age private identity file.}"
[[ -r "$AGE_IDENTITY_FILE" ]] || fail "Age identity file is not readable."

for command_name in age sha256sum docker; do
  command -v "$command_name" >/dev/null 2>&1 || fail "Required command not found: $command_name"
done

backup_dir="$(cd -- "$1" && pwd -P)"
[[ -f "$backup_dir/manifest.txt" && -f "$backup_dir/SHA256SUMS" ]] || fail "Manifest or checksum file is missing."
grep -qx 'format=travelos-backup-v1' "$backup_dir/manifest.txt" || fail "Unsupported backup manifest format."

(
  cd "$backup_dir"
  sha256sum --check --status SHA256SUMS
) || fail "One or more encrypted backup files failed SHA-256 verification."

verification_dir="$(mktemp -d)"
chmod 700 "$verification_dir"
cleanup() {
  status=$?
  trap - EXIT
  rm -rf -- "$verification_dir"
  exit "$status"
}
trap cleanup EXIT

age --decrypt --identity "$AGE_IDENTITY_FILE" --output "$verification_dir/database.dump" "$backup_dir/database.dump.age"
age --decrypt --identity "$AGE_IDENTITY_FILE" --output "$verification_dir/receipts.tar.gz" "$backup_dir/receipts.tar.gz.age"

docker run --rm --network none --volume "$verification_dir:/verify:ro" \
  postgres:16.6-bookworm pg_restore --list /verify/database.dump >/dev/null \
  || fail "Decrypted database dump is not a valid PostgreSQL custom archive."

docker run --rm --network none --volume "$verification_dir:/verify:ro" alpine:3.20 \
  tar -tzf /verify/receipts.tar.gz >/dev/null \
  || fail "Decrypted receipt archive is not a valid tar.gz archive."

echo "Backup integrity verified: checksums, age decryption, PostgreSQL archive and receipt archive."
echo "This check does not replace a full restore into a disposable PostgreSQL instance."
