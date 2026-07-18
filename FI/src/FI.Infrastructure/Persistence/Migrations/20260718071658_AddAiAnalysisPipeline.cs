using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnalysisPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_analysis_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parse_success = table.Column<bool>(type: "boolean", nullable: false),
                    schema_echo_mismatch = table.Column<bool>(type: "boolean", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    out_of_evidence_claims_detected = table.Column<bool>(type: "boolean", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_analysis_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_analysis_logs_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_analysis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    final_content = table.Column<string>(type: "jsonb", nullable: true),
                    reviewer_notes = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_incident_reviews_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    system_prompt_template = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rollout_percentage = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_analyses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    incident_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    probable_root_cause = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_refs = table.Column<string>(type: "jsonb", nullable: false),
                    recommended_actions = table.Column<string>(type: "jsonb", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    needs_human_review = table.Column<bool>(type: "boolean", nullable: false),
                    out_of_evidence_claims_detected = table.Column<bool>(type: "boolean", nullable: false),
                    is_latest = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_analyses", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_analyses_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_analyses_prompt_versions_prompt_version_id",
                        column: x => x.prompt_version_id,
                        principalTable: "prompt_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_analyses_incident_id_is_latest",
                table: "ai_analyses",
                columns: new[] { "incident_id", "is_latest" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_analyses_prompt_version_id",
                table: "ai_analyses",
                column: "prompt_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_analysis_logs_incident_id_created_at",
                table: "ai_analysis_logs",
                columns: new[] { "incident_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_incident_reviews_incident_id_reviewed_at",
                table: "incident_reviews",
                columns: new[] { "incident_id", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_prompt_versions_version_label",
                table: "prompt_versions",
                column: "version_label",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_analyses");

            migrationBuilder.DropTable(
                name: "ai_analysis_logs");

            migrationBuilder.DropTable(
                name: "incident_reviews");

            migrationBuilder.DropTable(
                name: "prompt_versions");
        }
    }
}
