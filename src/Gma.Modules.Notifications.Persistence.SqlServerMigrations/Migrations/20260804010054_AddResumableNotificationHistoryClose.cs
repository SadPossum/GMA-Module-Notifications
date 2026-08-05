using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddResumableNotificationHistoryClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_history_batch_close_operations",
                schema: "notifications",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpectedVersion = table.Column<long>(type: "bigint", nullable: false),
                    ResultingVersion = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "int", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "int", nullable: false),
                    ProofVersion = table.Column<int>(type: "int", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_history_batch_close_operations", x => new { x.ScopeId, x.OperationId });
                    table.CheckConstraint("CK_notification_history_batch_close_operations_batch", "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000");
                    table.CheckConstraint("CK_notification_history_batch_close_operations_progress", "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"ProofVersion\" >= 1");
                    table.CheckConstraint("CK_notification_history_batch_close_operations_versions", "\"ExpectedVersion\" >= 0 AND \"ResultingVersion\" > \"ExpectedVersion\"");
                    table.ForeignKey(
                        name: "FK_notification_history_batch_close_operations_notification_history_reference_states_ScopeId_Namespace_Digest",
                        columns: x => new { x.ScopeId, x.Namespace, x.Digest },
                        principalSchema: "notifications",
                        principalTable: "notification_history_reference_states",
                        principalColumns: new[] { "ScopeId", "Namespace", "Digest" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_history_batch_close_receipts",
                schema: "notifications",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ResultingVersion = table.Column<long>(type: "bigint", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "int", nullable: false),
                    RemovalProofVersion = table.Column<int>(type: "int", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_history_batch_close_receipts", x => new { x.ScopeId, x.OperationId });
                    table.CheckConstraint("CK_notification_history_batch_close_receipts_progress", "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"RemovalProofVersion\" >= 1");
                    table.CheckConstraint("CK_notification_history_batch_close_receipts_version", "\"ResultingVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_notification_history_batch_close_receipts_notification_history_reference_states_ScopeId_Namespace_Digest",
                        columns: x => new { x.ScopeId, x.Namespace, x.Digest },
                        principalSchema: "notifications",
                        principalTable: "notification_history_reference_states",
                        principalColumns: new[] { "ScopeId", "Namespace", "Digest" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_batch_close_operations_ScopeId_Namespace_Digest",
                schema: "notifications",
                table: "notification_history_batch_close_operations",
                columns: new[] { "ScopeId", "Namespace", "Digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_batch_close_receipts_ScopeId_Namespace_Digest",
                schema: "notifications",
                table: "notification_history_batch_close_receipts",
                columns: new[] { "ScopeId", "Namespace", "Digest" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [notifications].[notification_history_batch_close_receipts_append_only]
                ON [notifications].[notification_history_batch_close_receipts]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000,
                        'notification history batch close receipts are append-only',
                        1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [notifications].[notification_history_batch_close_receipts_append_only];
                """);

            migrationBuilder.DropTable(
                name: "notification_history_batch_close_operations",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_history_batch_close_receipts",
                schema: "notifications");
        }
    }
}
