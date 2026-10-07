using System;
using Microsoft.EntityFrameworkCore.Migrations;
using SharedKernel.Persistence.EfCore;

#nullable disable

namespace Shop.Billing.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    country = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: false
                    ),
                    vat_number = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    bic = table.Column<string>(
                        type: "character varying(11)",
                        maxLength: 11,
                        nullable: true
                    ),
                    iban_envelope = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
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
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_profiles", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_email = table.Column<string>(
                        type: "character varying(320)",
                        maxLength: 320,
                        nullable: true
                    ),
                    provider_reference = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    invoice = table.Column<byte[]>(type: "bytea", nullable: false),
                    invoice_signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    invoice_signing_key_id = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount_amount = table.Column<decimal>(
                        type: "numeric(19,4)",
                        precision: 19,
                        scale: 4,
                        nullable: false
                    ),
                    amount_currency = table.Column<string>(
                        type: "character(3)",
                        fixedLength: true,
                        maxLength: 3,
                        nullable: false
                    ),
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
                    table.PrimaryKey("pk_payments", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_billing_profiles_tenant_id",
                table: "billing_profiles",
                column: "tenant_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_customer_email",
                table: "payments",
                columns: new[] { "tenant_id", "customer_email" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_order_id",
                table: "payments",
                columns: new[] { "tenant_id", "order_id" },
                unique: true
            );

            // FORCE row-level security and the tenant policy on every tenant table of the model.
            migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "billing_profiles");

            migrationBuilder.DropTable(name: "payments");
        }
    }
}
