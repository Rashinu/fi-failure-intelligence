using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestionEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deployments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    commit = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changed_config = table.Column<string>(type: "jsonb", nullable: true),
                    deployed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployments", x => x.id);
                    table.ForeignKey(
                        name: "FK_deployments_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_idempotency_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_idempotency_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    request_redacted = table.Column<string>(type: "jsonb", nullable: true),
                    response_redacted = table.Column<string>(type: "jsonb", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_signature_verified = table.Column<bool>(type: "boolean", nullable: true),
                    payload_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_events", x => x.id);
                    table.CheckConstraint("ck_events_status_code", "status_code BETWEEN 100 AND 599");
                    table.ForeignKey(
                        name: "FK_integration_events_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deployments_integration_id",
                table: "deployments",
                column: "integration_id");

            migrationBuilder.CreateIndex(
                name: "IX_deployments_service_environment_deployed_at",
                table: "deployments",
                columns: new[] { "service", "environment", "deployed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_idempotency_keys_integration_id_idempotency_key",
                table: "ingestion_idempotency_keys",
                columns: new[] { "integration_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_events_correlation_id",
                table: "integration_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_integration_events_integration_id_occurred_at",
                table: "integration_events",
                columns: new[] { "integration_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_created_at",
                table: "outbox_messages",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deployments");

            migrationBuilder.DropTable(
                name: "ingestion_idempotency_keys");

            migrationBuilder.DropTable(
                name: "integration_events");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
