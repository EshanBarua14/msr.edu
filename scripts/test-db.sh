#!/usr/bin/env bash
# Apply all migrations to a scratch PostgreSQL database and run the SQL tests.
# Usage: DATABASE_URL=postgres://user:pass@host:5432/postgres scripts/test-db.sh
set -euo pipefail
cd "$(dirname "$0")/.."

: "${DATABASE_URL:?set DATABASE_URL to a disposable PostgreSQL database}"
DB=msr_test_$$
psql "$DATABASE_URL" -qXc "create database $DB" >/dev/null
trap 'psql "$DATABASE_URL" -qXc "drop database if exists $DB with (force)" >/dev/null' EXIT
TEST_URL="${DATABASE_URL%/*}/$DB"

run() { psql "$TEST_URL" -qX -v ON_ERROR_STOP=1 -f "$1" >/dev/null; }

run supabase/tests/00_supabase_stub.sql
for f in supabase/migrations/*.sql; do
  echo "migrate  $f"
  run "$f"
done
for f in supabase/tests/[1-9]*.sql; do
  echo "test     $f"
  psql "$TEST_URL" -qX -v ON_ERROR_STOP=1 -o /dev/null -f "$f" 2>&1 | sed -e 's/^psql:[^ ]* NOTICE:  /  /'
done
echo "database tests passed"
