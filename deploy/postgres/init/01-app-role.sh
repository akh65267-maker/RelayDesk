#!/bin/sh
# Runs once on first database init. Creates the non-owner app role (ADR 0003):
# no superuser, no BYPASSRLS, so row-level security policies always apply to it.
set -e
psql -v ON_ERROR_STOP=1 -U postgres -d relaydesk <<SQL
CREATE ROLE relay_app LOGIN PASSWORD '${POSTGRES_APP_PASSWORD}' NOSUPERUSER NOBYPASSRLS;
GRANT CONNECT ON DATABASE relaydesk TO relay_app;
CREATE EXTENSION IF NOT EXISTS vector;
SQL
