#!/bin/sh
# Runs once on first database init. Creates the non-owner app role (ADR 0003):
# no superuser, no BYPASSRLS, so row-level security policies always apply to it.
# Schema and table grants are applied by each module's migrations.
set -e
psql -v ON_ERROR_STOP=1 -U "${POSTGRES_USER:-postgres}" -d postgres <<SQL
CREATE ROLE relay_app LOGIN PASSWORD '${POSTGRES_APP_PASSWORD}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
SQL
