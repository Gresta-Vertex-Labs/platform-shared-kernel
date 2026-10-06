-- Grants per database: the runtime and cross-tenant roles read and write what app_migrator creates.
\c catalog
GRANT CONNECT ON DATABASE catalog TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;

\c ordering
GRANT CONNECT ON DATABASE ordering TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;

\c inventory
GRANT CONNECT ON DATABASE inventory TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;

\c billing
GRANT CONNECT ON DATABASE billing TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;

\c reports
GRANT CONNECT ON DATABASE reports TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;

