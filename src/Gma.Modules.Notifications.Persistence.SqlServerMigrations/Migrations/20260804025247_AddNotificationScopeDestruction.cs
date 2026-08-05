using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationScopeDestruction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_scope_destroy_operations",
                schema: "notifications",
                columns: table => new
                {
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "int", nullable: false),
                    ProofVersion = table.Column<int>(type: "int", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_scope_destroy_operations", x => x.ScopeId);
                    table.CheckConstraint("CK_notification_scope_destroy_operations_batch", "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000");
                    table.CheckConstraint("CK_notification_scope_destroy_operations_progress", "\"Stage\" >= 1 AND \"Stage\" <= 7 AND \"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"ProofVersion\" >= 1");
                    table.CheckConstraint("CK_notification_scope_destroy_operations_revisions", "\"ExpectedRevision\" >= 0 AND \"ResultingRevision\" > \"ExpectedRevision\"");
                    table.ForeignKey(
                        name: "FK_notification_scope_destroy_operations_notification_scope_states_ScopeId",
                        column: x => x.ScopeId,
                        principalSchema: "notifications",
                        principalTable: "notification_scope_states",
                        principalColumn: "ScopeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_scope_destroy_receipts",
                schema: "notifications",
                columns: table => new
                {
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "int", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "int", nullable: false),
                    RemovalProofVersion = table.Column<int>(type: "int", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_scope_destroy_receipts", x => x.ScopeId);
                    table.CheckConstraint("CK_notification_scope_destroy_receipts_progress", "\"BatchSize\" >= 1 AND \"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"RemovalProofVersion\" >= 1");
                    table.CheckConstraint("CK_notification_scope_destroy_receipts_revisions", "\"ExpectedRevision\" >= 0 AND \"ResultingRevision\" > \"ExpectedRevision\"");
                    table.ForeignKey(
                        name: "FK_notification_scope_destroy_receipts_notification_scope_states_ScopeId",
                        column: x => x.ScopeId,
                        principalSchema: "notifications",
                        principalTable: "notification_scope_states",
                        principalColumn: "ScopeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [notifications].[notification_scope_destroy_receipts_append_only]
                ON [notifications].[notification_scope_destroy_receipts]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000,
                        'notification scope destruction receipts are append-only',
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
                    [notifications].[notification_scope_destroy_receipts_append_only];
                """);

            migrationBuilder.DropTable(
                name: "notification_scope_destroy_operations",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_scope_destroy_receipts",
                schema: "notifications");
        }
    }
}
