using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOrdinalScopeIdentity : Migration
    {
        private static readonly ScopeIndexDefinition[] ScopeIndexes =
        [
            new("IX_notification_broadcasts_Audience_ScopeId_OccurredAtUtc", "notification_broadcasts", ["Audience", "ScopeId", "OccurredAtUtc"]),
            new("IX_notification_broadcasts_Audience_ScopeId_StreamSequence", "notification_broadcasts", ["Audience", "ScopeId", "StreamSequence"]),
            new("IX_deliveries_ScopeId_Status_CreatedAtUtc", "deliveries", ["ScopeId", "Status", "CreatedAtUtc"]),
            new("IX_deliveries_ScopeId_NotificationId_DeliveryTag_Provider", "deliveries", ["ScopeId", "NotificationId", "DeliveryTag", "Provider"], true),
            new("IX_delivery_routes_ScopeId_DeliveryTag", "delivery_routes", ["ScopeId", "DeliveryTag"], true),
            new("IX_delivery_routes_ScopeId_Provider_IsActive", "delivery_routes", ["ScopeId", "Provider", "IsActive"]),
            new("IX_preferences_ScopeId_UserId_Enabled", "preferences", ["ScopeId", "UserId", "Enabled"]),
            new("IX_preferences_ScopeId_UserId_TagKey", "preferences", ["ScopeId", "UserId", "TagKey"], true),
            new("IX_tag_definitions_ScopeId_TagKey", "tag_definitions", ["ScopeId", "TagKey"], true),
            new("IX_tag_definitions_ScopeId_Kind_IsActive_TagKey", "tag_definitions", ["ScopeId", "Kind", "IsActive", "TagKey"]),
            new("IX_user_notifications_ScopeId_StreamSequence", "user_notifications", ["ScopeId", "StreamSequence"]),
            new("IX_user_notifications_ScopeId_IsInboxVisible_CreatedAtUtc", "user_notifications", ["ScopeId", "IsInboxVisible", "CreatedAtUtc"]),
            new("IX_user_notifications_ScopeId_UserId_OccurredAtUtc", "user_notifications", ["ScopeId", "UserId", "OccurredAtUtc"]),
            new("IX_user_notifications_ScopeId_UserId_ReadAtUtc", "user_notifications", ["ScopeId", "UserId", "ReadAtUtc"]),
            new("IX_user_notifications_ScopeId_UserId_StreamSequence", "user_notifications", ["ScopeId", "UserId", "StreamSequence"]),
            new("IX_delivery_attempts_ScopeId_CompletedAtUtc", "delivery_attempts", ["ScopeId", "CompletedAtUtc"]),
            new("IX_delivery_attempts_ScopeId_DeliveryId_AttemptNumber", "delivery_attempts", ["ScopeId", "DeliveryId", "AttemptNumber"], true),
            new("IX_notification_history_batch_close_operations_ScopeId_Namespace_Digest", "notification_history_batch_close_operations", ["ScopeId", "Namespace", "Digest"], true),
            new("IX_notification_history_batch_close_receipts_ScopeId_Namespace_Digest", "notification_history_batch_close_receipts", ["ScopeId", "Namespace", "Digest"], true),
            new("IX_notification_history_close_receipts_ScopeId_Namespace_Digest_CompletedAtUtc", "notification_history_close_receipts", ["ScopeId", "Namespace", "Digest", "CompletedAtUtc"]),
            new("IX_notification_history_reference_states_ScopeId_IsClosed_ClosedAtUtc", "notification_history_reference_states", ["ScopeId", "IsClosed", "ClosedAtUtc"]),
            new("IX_notification_scope_states_IsClosed_ClosedAtUtc_ScopeId", "notification_scope_states", ["IsClosed", "ClosedAtUtc", "ScopeId"]),
            new("IX_user_notification_references_ScopeId_Namespace_Digest_NotificationId", "user_notification_references", ["ScopeId", "Namespace", "Digest", "NotificationId"]),
            new("IX_user_notification_tags_ScopeId_TagKey_NotificationId", "user_notification_tags", ["ScopeId", "TagKey", "NotificationId"])
        ];

        private static readonly ScopePrimaryKeyDefinition[] ScopePrimaryKeys =
        [
            new("PK_notification_history_batch_close_operations", "notification_history_batch_close_operations", ["ScopeId", "OperationId"]),
            new("PK_notification_history_batch_close_receipts", "notification_history_batch_close_receipts", ["ScopeId", "OperationId"]),
            new("PK_notification_history_close_receipts", "notification_history_close_receipts", ["ScopeId", "OperationId"]),
            new("PK_notification_history_reference_states", "notification_history_reference_states", ["ScopeId", "Namespace", "Digest"]),
            new("PK_notification_scope_destroy_operations", "notification_scope_destroy_operations", ["ScopeId"]),
            new("PK_notification_scope_destroy_receipts", "notification_scope_destroy_receipts", ["ScopeId"]),
            new("PK_notification_scope_states", "notification_scope_states", ["ScopeId"]),
            new("PK_user_notification_references", "user_notification_references", ["ScopeId", "NotificationId", "Namespace", "Digest"]),
            new("PK_user_notification_tags", "user_notification_tags", ["ScopeId", "NotificationId", "TagKey"])
        ];

        private static readonly ScopeForeignKeyDefinition[] ScopeForeignKeys =
        [
            new("FK_notification_history_batch_close_operations_notification_history_reference_states_ScopeId_Namespace_Digest", "notification_history_batch_close_operations", ["ScopeId", "Namespace", "Digest"], "notification_history_reference_states", ["ScopeId", "Namespace", "Digest"]),
            new("FK_notification_history_batch_close_receipts_notification_history_reference_states_ScopeId_Namespace_Digest", "notification_history_batch_close_receipts", ["ScopeId", "Namespace", "Digest"], "notification_history_reference_states", ["ScopeId", "Namespace", "Digest"]),
            new("FK_notification_history_close_receipts_notification_history_reference_states_ScopeId_Namespace_Digest", "notification_history_close_receipts", ["ScopeId", "Namespace", "Digest"], "notification_history_reference_states", ["ScopeId", "Namespace", "Digest"]),
            new("FK_notification_scope_destroy_operations_notification_scope_states_ScopeId", "notification_scope_destroy_operations", ["ScopeId"], "notification_scope_states", ["ScopeId"]),
            new("FK_notification_scope_destroy_receipts_notification_scope_states_ScopeId", "notification_scope_destroy_receipts", ["ScopeId"], "notification_scope_states", ["ScopeId"]),
            new("FK_user_notification_references_notification_history_reference_states_ScopeId_Namespace_Digest", "user_notification_references", ["ScopeId", "Namespace", "Digest"], "notification_history_reference_states", ["ScopeId", "Namespace", "Digest"])
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropScopeDependencies(migrationBuilder);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "user_notifications",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "user_notification_tags",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "user_notification_references",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "tag_definitions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "preferences",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_scope_states",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_scope_destroy_receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_scope_destroy_operations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_reference_states",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_close_receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_batch_close_receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_batch_close_operations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_broadcasts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "inbox_messages",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "delivery_routes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "delivery_attempts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "deliveries",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            CreateScopeDependencies(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropScopeDependencies(migrationBuilder);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "user_notifications",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "user_notification_tags",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "user_notification_references",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "tag_definitions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "preferences",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_scope_states",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_scope_destroy_receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_scope_destroy_operations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_reference_states",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_close_receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_batch_close_receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_history_batch_close_operations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "notification_broadcasts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "inbox_messages",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "delivery_routes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "delivery_attempts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "notifications",
                table: "deliveries",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            CreateScopeDependencies(migrationBuilder);
        }

        private static void DropScopeDependencies(MigrationBuilder migrationBuilder)
        {
            foreach (ScopeForeignKeyDefinition foreignKey in ScopeForeignKeys)
            {
                migrationBuilder.DropForeignKey(
                    name: foreignKey.Name,
                    schema: "notifications",
                    table: foreignKey.Table);
            }

            foreach (ScopeIndexDefinition index in ScopeIndexes)
            {
                migrationBuilder.DropIndex(
                    name: index.Name,
                    schema: "notifications",
                    table: index.Table);
            }

            foreach (ScopePrimaryKeyDefinition primaryKey in ScopePrimaryKeys)
            {
                migrationBuilder.DropPrimaryKey(
                    name: primaryKey.Name,
                    schema: "notifications",
                    table: primaryKey.Table);
            }
        }

        private static void CreateScopeDependencies(MigrationBuilder migrationBuilder)
        {
            foreach (ScopePrimaryKeyDefinition primaryKey in ScopePrimaryKeys)
            {
                migrationBuilder.AddPrimaryKey(
                    name: primaryKey.Name,
                    schema: "notifications",
                    table: primaryKey.Table,
                    columns: primaryKey.Columns);
            }

            foreach (ScopeIndexDefinition index in ScopeIndexes)
            {
                migrationBuilder.CreateIndex(
                    name: index.Name,
                    schema: "notifications",
                    table: index.Table,
                    columns: index.Columns,
                    unique: index.Unique);
            }

            foreach (ScopeForeignKeyDefinition foreignKey in ScopeForeignKeys)
            {
                migrationBuilder.AddForeignKey(
                    name: foreignKey.Name,
                    schema: "notifications",
                    table: foreignKey.Table,
                    columns: foreignKey.Columns,
                    principalSchema: "notifications",
                    principalTable: foreignKey.PrincipalTable,
                    principalColumns: foreignKey.PrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
            }
        }

        private sealed record ScopeIndexDefinition(
            string Name,
            string Table,
            string[] Columns,
            bool Unique = false);

        private sealed record ScopePrimaryKeyDefinition(
            string Name,
            string Table,
            string[] Columns);

        private sealed record ScopeForeignKeyDefinition(
            string Name,
            string Table,
            string[] Columns,
            string PrincipalTable,
            string[] PrincipalColumns);
    }
}
