using System;
using Microsoft.EntityFrameworkCore.Migrations;
using SharedKernel.Persistence.EfCore;

#nullable disable

namespace Shop.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    description = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    brand = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    category = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    image_key = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    price_amount = table.Column<decimal>(
                        type: "numeric(19,4)",
                        precision: 19,
                        scale: 4,
                        nullable: false
                    ),
                    price_currency = table.Column<string>(
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
                    table.PrimaryKey("pk_products", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_sku",
                table: "products",
                columns: new[] { "tenant_id", "sku" },
                unique: true
            );

            // FORCE row-level security and the tenant policy on every tenant table (checked again at startup).
            migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "products");
        }
    }
}
