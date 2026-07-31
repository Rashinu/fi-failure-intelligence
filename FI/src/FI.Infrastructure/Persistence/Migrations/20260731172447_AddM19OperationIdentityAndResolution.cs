using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddM19OperationIdentityAndResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_record_ref",
                table: "integration_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation_ref",
                table: "integration_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation_type",
                table: "integration_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                table: "incidents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolved_by",
                table: "incidents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "business_record_ref",
                table: "integration_events");

            migrationBuilder.DropColumn(
                name: "operation_ref",
                table: "integration_events");

            migrationBuilder.DropColumn(
                name: "operation_type",
                table: "integration_events");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "resolved_by",
                table: "incidents");
        }
    }
}
