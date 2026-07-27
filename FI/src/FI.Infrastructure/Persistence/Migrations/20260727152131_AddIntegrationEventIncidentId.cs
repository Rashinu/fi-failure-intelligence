using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationEventIncidentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "incident_id",
                table: "integration_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_events_incident_id",
                table: "integration_events",
                column: "incident_id");

            migrationBuilder.AddForeignKey(
                name: "FK_integration_events_incidents_incident_id",
                table: "integration_events",
                column: "incident_id",
                principalTable: "incidents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_integration_events_incidents_incident_id",
                table: "integration_events");

            migrationBuilder.DropIndex(
                name: "IX_integration_events_incident_id",
                table: "integration_events");

            migrationBuilder.DropColumn(
                name: "incident_id",
                table: "integration_events");
        }
    }
}
