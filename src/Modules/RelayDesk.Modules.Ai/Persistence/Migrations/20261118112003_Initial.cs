using System;
using Microsoft.EntityFrameworkCore.Migrations;
using RelayDesk.SharedKernel.Persistence;

#nullable disable

namespace RelayDesk.Modules.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "suggestions",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_tenant_id_ticket_id_created_at",
                schema: "ai",
                table: "suggestions",
                columns: new[] { "tenant_id", "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_tenant_id_occurred_at",
                schema: "ai",
                table: "usage",
                columns: new[] { "tenant_id", "occurred_at" });

            // ADR 0003: suggestions and usage stay inside the current tenant.
            migrationBuilder.Sql(RlsSql.TenantIsolation("ai", "suggestions"));
            migrationBuilder.Sql(RlsSql.TenantIsolation("ai", "usage"));
            migrationBuilder.Sql(RlsSql.GrantSchema("ai"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suggestions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "usage",
                schema: "ai");
        }
    }
}
