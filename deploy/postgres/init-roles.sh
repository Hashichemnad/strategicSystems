#!/bin/sh
# Runs once, on the first start of an empty PostgreSQL volume (docker-entrypoint-initdb.d).
# Creates the runtime role the application connects as (ADR 0018). The owner role (POSTGRES_USER) owns the schemas and runs the
# migrations; after every `--migrate` the application grants this role read / write on the module schemas and only SELECT + INSERT
# on audit.entry.
set -eu
: "${STRATEGY_APP_USER:=strategy_app}"
: "${STRATEGY_APP_PASSWORD:=strategy_app}"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v app_user="$STRATEGY_APP_USER" -v app_password="$STRATEGY_APP_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_user', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user') \gexec
SELECT format('GRANT CONNECT, TEMPORARY ON DATABASE %I TO %I', current_database(), :'app_user') \gexec
SQL
