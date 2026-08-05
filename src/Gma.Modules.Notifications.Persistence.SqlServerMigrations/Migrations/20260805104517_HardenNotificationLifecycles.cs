using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class HardenNotificationLifecycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_scope_destroy_receipts_progress",
                schema: "notifications",
                table: "notification_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_scope_destroy_operations_progress",
                schema: "notifications",
                table: "notification_scope_destroy_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_history_batch_close_receipts_progress",
                schema: "notifications",
                table: "notification_history_batch_close_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_history_batch_close_operations_progress",
                schema: "notifications",
                table: "notification_history_batch_close_operations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_scope_states_closure",
                schema: "notifications",
                table: "notification_scope_states",
                sql: "(CAST(\"IsClosed\" AS integer) = 0 AND \"CloseOperationId\" IS NULL AND \"CloseRequestSha256\" IS NULL AND \"ClosedAtUtc\" IS NULL) OR (CAST(\"IsClosed\" AS integer) = 1 AND \"Version\" >= 1 AND \"CloseOperationId\" IS NOT NULL AND \"CloseRequestSha256\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_scope_destroy_receipts_progress",
                schema: "notifications",
                table: "notification_scope_destroy_receipts",
                sql: "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000 AND ((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"RemovalProofVersion\" = 1 AND \"CompletedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_scope_destroy_operations_progress",
                schema: "notifications",
                table: "notification_scope_destroy_operations",
                sql: "\"Stage\" >= 1 AND \"Stage\" <= 7 AND ((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"ProofVersion\" = 1 AND \"UpdatedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_history_reference_states_closure",
                schema: "notifications",
                table: "notification_history_reference_states",
                sql: "(CAST(\"IsClosed\" AS integer) = 0 AND \"CloseOperationId\" IS NULL AND \"CloseRequestSha256\" IS NULL AND \"ClosedAtUtc\" IS NULL) OR (CAST(\"IsClosed\" AS integer) = 1 AND \"Version\" >= 1 AND \"CloseOperationId\" IS NOT NULL AND \"CloseRequestSha256\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_history_batch_close_receipts_progress",
                schema: "notifications",
                table: "notification_history_batch_close_receipts",
                sql: "((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"RemovalProofVersion\" = 1 AND \"CompletedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_history_batch_close_operations_progress",
                schema: "notifications",
                table: "notification_history_batch_close_operations",
                sql: "((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"ProofVersion\" = 1 AND \"UpdatedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_history_close_receipts_notification_history_reference_states_ScopeId_Namespace_Digest",
                schema: "notifications",
                table: "notification_history_close_receipts",
                columns: new[] { "ScopeId", "Namespace", "Digest" },
                principalSchema: "notifications",
                principalTable: "notification_history_reference_states",
                principalColumns: new[] { "ScopeId", "Namespace", "Digest" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [notifications].[notification_scope_states_closed_immutable]
                ON [notifications].[notification_scope_states]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted
                        WHERE [IsClosed] = 1)
                    BEGIN
                        THROW 51000,
                            'closed notification scope state is immutable',
                            1;
                    END;
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [notifications].[notification_history_reference_states_closed_immutable]
                ON [notifications].[notification_history_reference_states]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted
                        WHERE [IsClosed] = 1)
                    BEGIN
                        THROW 51000,
                            'closed notification history reference state is immutable',
                            1;
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [notifications].[notification_scope_states_closed_immutable];
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [notifications].[notification_history_reference_states_closed_immutable];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_notification_history_close_receipts_notification_history_reference_states_ScopeId_Namespace_Digest",
                schema: "notifications",
                table: "notification_history_close_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_scope_states_closure",
                schema: "notifications",
                table: "notification_scope_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_scope_destroy_receipts_progress",
                schema: "notifications",
                table: "notification_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_scope_destroy_operations_progress",
                schema: "notifications",
                table: "notification_scope_destroy_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_history_reference_states_closure",
                schema: "notifications",
                table: "notification_history_reference_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_history_batch_close_receipts_progress",
                schema: "notifications",
                table: "notification_history_batch_close_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_history_batch_close_operations_progress",
                schema: "notifications",
                table: "notification_history_batch_close_operations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_scope_destroy_receipts_progress",
                schema: "notifications",
                table: "notification_scope_destroy_receipts",
                sql: "\"BatchSize\" >= 1 AND \"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"RemovalProofVersion\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_scope_destroy_operations_progress",
                schema: "notifications",
                table: "notification_scope_destroy_operations",
                sql: "\"Stage\" >= 1 AND \"Stage\" <= 7 AND \"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"ProofVersion\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_history_batch_close_receipts_progress",
                schema: "notifications",
                table: "notification_history_batch_close_receipts",
                sql: "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"RemovalProofVersion\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_history_batch_close_operations_progress",
                schema: "notifications",
                table: "notification_history_batch_close_operations",
                sql: "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"ProofVersion\" >= 1");
        }
    }
}
