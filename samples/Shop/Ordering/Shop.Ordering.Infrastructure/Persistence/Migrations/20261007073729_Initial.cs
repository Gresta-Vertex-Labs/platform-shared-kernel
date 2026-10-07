using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SharedKernel.Persistence.EfCore;

#nullable disable

namespace Shop.Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_state",
                columns: table => new
                {
                    id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(
                        type: "bytea",
                        rowVersion: true,
                        nullable: true
                    ),
                    received = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    receive_count = table.Column<int>(type: "integer", nullable: false),
                    expiration_time = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    consumed = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    delivered = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_state", x => x.id);
                    table.UniqueConstraint(
                        "ak_inbox_state_message_id_consumer_id",
                        x => new { x.message_id, x.consumer_id }
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_email = table.Column<string>(
                        type: "character varying(2087)",
                        maxLength: 2087,
                        nullable: false
                    ),
                    shipping_address = table.Column<string>(
                        type: "character varying(3047)",
                        maxLength: 3047,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    customer_email_blind_index = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    created_on = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    modified_by = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    modified_on = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_state",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(
                        type: "bytea",
                        rowVersion: true,
                        nullable: true
                    ),
                    created = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    delivered = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_state", x => x.outbox_id);
                }
            );

            migrationBuilder.CreateTable(
                name: "totp_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    secret_base32 = table.Column<string>(
                        type: "character varying(722)",
                        maxLength: 722,
                        nullable: false
                    ),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    created_on = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    modified_by = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    modified_on = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_totp_enrollments", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price_amount = table.Column<decimal>(
                        type: "numeric(19,4)",
                        precision: 19,
                        scale: 4,
                        nullable: false
                    ),
                    unit_price_currency = table.Column<string>(
                        type: "character(3)",
                        fixedLength: true,
                        maxLength: 3,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    sequence_number = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    enqueue_time = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    sent_time = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    headers = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    inbox_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbox_consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initiator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_address = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    destination_address = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    response_address = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    fault_address = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    expiration_time = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.sequence_number);
                    table.ForeignKey(
                        name: "fk_outbox_message_inbox_state_inbox_message_id_inbox_consumer_",
                        columns: x => new { x.inbox_message_id, x.inbox_consumer_id },
                        principalTable: "inbox_state",
                        principalColumns: new[] { "message_id", "consumer_id" }
                    );
                    table.ForeignKey(
                        name: "fk_outbox_message_outbox_state_outbox_id",
                        column: x => x.outbox_id,
                        principalTable: "outbox_state",
                        principalColumn: "outbox_id"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_inbox_state_delivered",
                table: "inbox_state",
                column: "delivered"
            );

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id",
                table: "order_lines",
                column: "order_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_tenant_id",
                table: "order_lines",
                column: "tenant_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_email_blind_index",
                table: "orders",
                column: "customer_email_blind_index"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id",
                table: "orders",
                column: "tenant_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_enqueue_time",
                table: "outbox_message",
                column: "enqueue_time"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_expiration_time",
                table: "outbox_message",
                column: "expiration_time"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_inbox_message_id_inbox_consumer_id_sequence_",
                table: "outbox_message",
                columns: new[] { "inbox_message_id", "inbox_consumer_id", "sequence_number" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_outbox_id_sequence_number",
                table: "outbox_message",
                columns: new[] { "outbox_id", "sequence_number" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_state_created",
                table: "outbox_state",
                column: "created"
            );

            migrationBuilder.CreateIndex(
                name: "ix_totp_enrollments_subject_id",
                table: "totp_enrollments",
                column: "subject_id",
                unique: true
            );

            // FORCE row-level security and the tenant policy on every tenant table of the model.
            migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);

            // The signed audit ledger: written by the runtime role, chained by the sealer role; the cross-tenant role
            // must not rewrite it either.
            migrationBuilder.CreateAuditLedgerTable(
                runtimeRole: "app_runtime",
                sealerRole: "app_audit_sealer"
            );
            migrationBuilder.Sql(
                "REVOKE UPDATE, DELETE, TRUNCATE ON audit_records, audit_record_payloads, audit_chain_links, audit_checkpoints FROM app_cross_tenant;"
            );

            // The EF Core idempotency store (message deduplication) owns its context and ships no migrations; this is
            // its table, generated from the package's model with `dotnet ef dbcontext script --context IdempotencyDbContext`.
            migrationBuilder.Sql(
                """
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
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "order_lines");

            migrationBuilder.DropTable(name: "outbox_message");

            migrationBuilder.DropTable(name: "totp_enrollments");

            migrationBuilder.DropTable(name: "orders");

            migrationBuilder.DropTable(name: "inbox_state");

            migrationBuilder.DropTable(name: "outbox_state");
        }
    }
}
