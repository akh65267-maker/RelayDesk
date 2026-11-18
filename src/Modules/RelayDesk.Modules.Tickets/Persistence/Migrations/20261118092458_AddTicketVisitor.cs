using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelayDesk.Modules.Tickets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketVisitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "visitor_id",
                schema: "tickets",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_visitor_id",
                schema: "tickets",
                table: "tickets",
                columns: new[] { "tenant_id", "visitor_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_tenant_id_visitor_id",
                schema: "tickets",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "visitor_id",
                schema: "tickets",
                table: "tickets");
        }
    }
}
