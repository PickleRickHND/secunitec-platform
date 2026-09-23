#!/bin/sh
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -v billing_password="$BILLING_DB_PASSWORD" -v identity_password="$IDENTITY_DB_PASSWORD" <<'SQL'
CREATE ROLE billing_app LOGIN PASSWORD :'billing_password';
CREATE ROLE identity_app LOGIN PASSWORD :'identity_password';
CREATE DATABASE secunitec_billing OWNER billing_app;
CREATE DATABASE secunitec_identity OWNER identity_app;
-- TB2: PUBLIC puede conectarse a cualquier base por defecto; cada rol queda limitado a la suya.
REVOKE ALL ON DATABASE secunitec_billing FROM PUBLIC;
REVOKE ALL ON DATABASE secunitec_identity FROM PUBLIC;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
SQL
