using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAffectedCustomerRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "affected_customer_ref",
                table: "integration_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "affected_customer_ref",
                table: "integration_events");
        }
    }
}
