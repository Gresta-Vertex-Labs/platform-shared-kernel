-- The canonical role layout of SharedKernel.Persistence.Npgsql, for every Shop database. Run once by the postgres image
-- on first start. DEVELOPMENT-ONLY passwords; the AppHost builds the connection strings from them.

-- Owns every table and runs migrations. Never used by a service at runtime.
CREATE ROLE app_migrator LOGIN PASSWORD 'migrator-dev' NOSUPERUSER;
-- The services: no superuser, no BYPASSRLS, owns nothing.
CREATE ROLE app_runtime LOGIN PASSWORD 'runtime-dev' NOSUPERUSER NOBYPASSRLS;
-- Cross-tenant work under row-level security (back office, maintenance).
CREATE ROLE app_cross_tenant LOGIN PASSWORD 'cross-tenant-dev' NOSUPERUSER BYPASSRLS;
-- The audit sealer of 06.Persistence's audit ledger.
CREATE ROLE app_audit_sealer LOGIN PASSWORD 'sealer-dev' NOSUPERUSER NOBYPASSRLS;

CREATE DATABASE catalog OWNER app_migrator;
CREATE DATABASE ordering OWNER app_migrator;
CREATE DATABASE inventory OWNER app_migrator;
CREATE DATABASE billing OWNER app_migrator;
CREATE DATABASE reports OWNER app_migrator;
