using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptVersionEvalTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "eval_overall_average",
                table: "prompt_versions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eval_per_dimension",
                table: "prompt_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "evaluated_at",
                table: "prompt_versions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "eval_overall_average",
                table: "prompt_versions");

            migrationBuilder.DropColumn(
                name: "eval_per_dimension",
                table: "prompt_versions");

            migrationBuilder.DropColumn(
                name: "evaluated_at",
                table: "prompt_versions");
        }
    }
}
