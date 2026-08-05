using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationScopeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_scope_states",
                schema: "notifications",
                columns: table => new
                {
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    CloseOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CloseRequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_scope_states", x => x.ScopeId);
                    table.CheckConstraint("CK_notification_scope_states_version", "\"Version\" >= 0");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [notifications].[notification_scope_states]
                    ([ScopeId], [Version], [IsClosed])
                SELECT scopes.[ScopeId], 1, 0
                FROM (
                    SELECT [ScopeId] FROM [notifications].[inbox_messages] WHERE [ScopeId] IS NOT NULL
                    UNION SELECT [ScopeId] FROM [notifications].[notification_broadcasts] WHERE [ScopeId] IS NOT NULL
                    UNION SELECT [ScopeId] FROM [notifications].[deliveries]
                    UNION SELECT [ScopeId] FROM [notifications].[delivery_routes]
                    UNION SELECT [ScopeId] FROM [notifications].[preferences]
                    UNION SELECT [ScopeId] FROM [notifications].[tag_definitions]
                    UNION SELECT [ScopeId] FROM [notifications].[user_notifications]
                    UNION SELECT SUBSTRING([RecipientScope], 8, 128) AS [ScopeId]
                        FROM [notifications].[notification_broadcast_reads]
                        WHERE [RecipientScope] LIKE N'tenant:%'
                    UNION SELECT [ScopeId] FROM [notifications].[delivery_attempts]
                    UNION SELECT [ScopeId] FROM [notifications].[notification_history_batch_close_operations]
                    UNION SELECT [ScopeId] FROM [notifications].[notification_history_batch_close_receipts]
                    UNION SELECT [ScopeId] FROM [notifications].[notification_history_close_receipts]
                    UNION SELECT [ScopeId] FROM [notifications].[notification_history_reference_states]
                    UNION SELECT [ScopeId] FROM [notifications].[user_notification_references]
                    UNION SELECT [ScopeId] FROM [notifications].[user_notification_tags]
                ) AS scopes;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_notification_scope_states_IsClosed_ClosedAtUtc_ScopeId",
                schema: "notifications",
                table: "notification_scope_states",
                columns: new[] { "IsClosed", "ClosedAtUtc", "ScopeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_scope_states",
                schema: "notifications");
        }
    }
}
