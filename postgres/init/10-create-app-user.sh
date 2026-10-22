#!/bin/sh
set -eu

: "${APP_DB_USER:?APP_DB_USER must be configured}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be configured}"

psql \
    --set=ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --set=app_db_user="$APP_DB_USER" \
    --set=app_db_password="$APP_DB_PASSWORD" <<'SQL'
CREATE ROLE :"app_db_user" LOGIN PASSWORD :'app_db_password';
GRANT USAGE, CREATE ON SCHEMA public TO :"app_db_user";
SQL
