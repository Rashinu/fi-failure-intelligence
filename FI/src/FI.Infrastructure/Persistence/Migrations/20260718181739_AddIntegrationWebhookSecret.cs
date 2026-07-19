using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationWebhookSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_secret",
                table: "integrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_secret",
                table: "integrations");
        }
    }
}
