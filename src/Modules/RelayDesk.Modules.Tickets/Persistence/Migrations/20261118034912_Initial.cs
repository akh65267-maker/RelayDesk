using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using RelayDesk.SharedKernel.Persistence;

#nullable disable

namespace RelayDesk.Modules.Tickets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tickets");

            migrationBuilder.CreateTable(
                name: "ticket_counters",
                schema: "tickets",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_counters", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assignee_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    requester_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    requester_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_messages",
                schema: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    author_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_messages_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "tickets",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_messages_ticket_id_created_at",
                schema: "tickets",
                table: "ticket_messages",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tags",
                schema: "tickets",
                table: "tickets",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_assignee_user_id",
                schema: "tickets",
                table: "tickets",
                columns: new[] { "tenant_id", "assignee_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_number",
                schema: "tickets",
                table: "tickets",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_status_updated_at",
                schema: "tickets",
                table: "tickets",
                columns: new[] { "tenant_id", "status", "updated_at" });

            // ADR 0003: every tickets table is visible and writable only inside the current tenant.
            migrationBuilder.Sql(RlsSql.TenantIsolation("tickets", "tickets"));
            migrationBuilder.Sql(RlsSql.TenantIsolation("tickets", "ticket_messages"));
            migrationBuilder.Sql(RlsSql.TenantIsolation("tickets", "ticket_counters"));
            migrationBuilder.Sql(RlsSql.GrantSchema("tickets"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_counters",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "ticket_messages",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "tickets");
        }
    }
}
