using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharedKernel.Idempotency.EfCore.Migrations
{
    /// <summary>
    /// Creates <c>idempotency_keys</c> exactly as the model declares it, unless the relation already exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard is <c>to_regclass</c>, not <c>CREATE ... IF NOT EXISTS</c>: PostgreSQL checks the schema <c>CREATE</c>
    /// privilege and table ownership before the existence test, so a hand-made table owned by another role would fail
    /// the migrator. When the relation exists nothing at all is executed (no <c>ALTER</c>, no <c>INSERT</c>, no index
    /// statement) and the shape of that table is not inspected.
    /// </para>
    /// <para><see cref="Down"/> drops the table and every row in it.</para>
    /// </remarks>
    public partial class InitialIdempotencySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $idempotency$
                BEGIN
                    IF to_regclass('idempotency_keys') IS NULL THEN
                        CREATE TABLE idempotency_keys (
                            tenant_scope character varying(36) NOT NULL,
                            purpose character varying(16) NOT NULL,
                            key character varying(512) NOT NULL,
                            fingerprint character varying(128) NOT NULL,
                            status character varying(20) NOT NULL,
                            reservation_token uuid NOT NULL,
                            reserved_at_utc timestamp with time zone NOT NULL,
                            expires_at_utc timestamp with time zone NOT NULL,
                            response text,
                            CONSTRAINT pk_idempotency_keys PRIMARY KEY (tenant_scope, purpose, key)
                        );
                        CREATE INDEX ix_idempotency_keys_expires_at_utc ON idempotency_keys (expires_at_utc);
                    END IF;
                END
                $idempotency$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys");
        }
    }
}
