-- The canonical role script of SharedKernel.Persistence.Npgsql (README → "Roles: the one canonical script"),
-- run once by the postgres image on first start. DEVELOPMENT-ONLY passwords.

-- Owns every table, runs migrations (MigrationConnectionString). Never used by the application at runtime.
CREATE ROLE app_migrator LOGIN PASSWORD 'migrator-dev' NOSUPERUSER;
-- The application (ConnectionStrings:billing): no superuser, no BYPASSRLS, owns nothing, member of nothing below.
CREATE ROLE app_runtime LOGIN PASSWORD 'runtime-dev' NOSUPERUSER NOBYPASSRLS;
-- Cross-tenant work under RLS (back-office reports, encryption maintenance, tenant erasure).
CREATE ROLE app_cross_tenant LOGIN PASSWORD 'cross-tenant-dev' NOSUPERUSER BYPASSRLS;
-- The audit sealer (Auditing:Sealer:DataSourceName).
CREATE ROLE app_audit_sealer LOGIN PASSWORD 'sealer-dev' NOSUPERUSER NOBYPASSRLS;

CREATE DATABASE billing OWNER app_migrator;
\c billing
GRANT CONNECT ON DATABASE billing TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;

-- Every table and sequence app_migrator creates from now on:
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;
