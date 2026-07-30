using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationHistoryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_history_close_receipts",
                schema: "notifications",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ResultingVersion = table.Column<long>(type: "bigint", nullable: false),
                    RemovedRecordCount = table.Column<int>(type: "int", nullable: false),
                    RemovedRecordIdsSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_history_close_receipts", x => new { x.ScopeId, x.OperationId });
                    table.CheckConstraint("CK_notification_history_close_receipts_count", "\"RemovedRecordCount\" >= 0");
                    table.CheckConstraint("CK_notification_history_close_receipts_version", "\"ResultingVersion\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "notification_history_reference_states",
                schema: "notifications",
                columns: table => new
                {
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CloseOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CloseRequestSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_history_reference_states", x => new { x.ScopeId, x.Namespace, x.Digest });
                    table.CheckConstraint("CK_notification_history_reference_states_version", "\"Version\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "user_notification_references",
                schema: "notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notification_references", x => new { x.ScopeId, x.NotificationId, x.Namespace, x.Digest });
                    table.ForeignKey(
                        name: "FK_user_notification_references_notification_history_reference_states_ScopeId_Namespace_Digest",
                        columns: x => new { x.ScopeId, x.Namespace, x.Digest },
                        principalSchema: "notifications",
                        principalTable: "notification_history_reference_states",
                        principalColumns: new[] { "ScopeId", "Namespace", "Digest" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_notification_references_user_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalSchema: "notifications",
                        principalTable: "user_notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_close_receipts_ScopeId_Namespace_Digest_CompletedAtUtc",
                schema: "notifications",
                table: "notification_history_close_receipts",
                columns: new[] { "ScopeId", "Namespace", "Digest", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_reference_states_ScopeId_IsClosed_ClosedAtUtc",
                schema: "notifications",
                table: "notification_history_reference_states",
                columns: new[] { "ScopeId", "IsClosed", "ClosedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_notification_references_NotificationId",
                schema: "notifications",
                table: "user_notification_references",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_notification_references_ScopeId_Namespace_Digest_NotificationId",
                schema: "notifications",
                table: "user_notification_references",
                columns: new[] { "ScopeId", "Namespace", "Digest", "NotificationId" });

            migrationBuilder.Sql(
                """
                WITH recipient_references AS
                (
                    SELECT
                        notification.[Id] AS [NotificationId],
                        notification.[ScopeId],
                        LOWER(
                            CONVERT(
                                varchar(64),
                                HASHBYTES(
                                    'SHA2_256',
                                    CONVERT(
                                        varchar(max),
                                        CONCAT(
                                            N'gma-notification-recipient/v1|',
                                            notification.[ScopeId],
                                            N'|',
                                            notification.[UserId])
                                        COLLATE Latin1_General_100_BIN2_UTF8)),
                                2)) AS [Digest]
                    FROM [notifications].[user_notifications] AS notification
                )
                INSERT INTO [notifications].[notification_history_reference_states]
                    ([ScopeId], [Namespace], [Digest], [Version], [IsClosed])
                SELECT
                    reference.[ScopeId],
                    N'recipient',
                    reference.[Digest],
                    COUNT_BIG(*),
                    CAST(0 AS bit)
                FROM recipient_references AS reference
                GROUP BY
                    reference.[ScopeId],
                    reference.[Digest];

                WITH recipient_references AS
                (
                    SELECT
                        notification.[Id] AS [NotificationId],
                        notification.[ScopeId],
                        LOWER(
                            CONVERT(
                                varchar(64),
                                HASHBYTES(
                                    'SHA2_256',
                                    CONVERT(
                                        varchar(max),
                                        CONCAT(
                                            N'gma-notification-recipient/v1|',
                                            notification.[ScopeId],
                                            N'|',
                                            notification.[UserId])
                                        COLLATE Latin1_General_100_BIN2_UTF8)),
                                2)) AS [Digest]
                    FROM [notifications].[user_notifications] AS notification
                )
                INSERT INTO [notifications].[user_notification_references]
                    ([NotificationId], [ScopeId], [Namespace], [Digest])
                SELECT
                    reference.[NotificationId],
                    reference.[ScopeId],
                    N'recipient',
                    reference.[Digest]
                FROM recipient_references AS reference;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [notifications].[notification_history_close_receipts_append_only]
                ON [notifications].[notification_history_close_receipts]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000,
                        'notification history close receipts are append-only',
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
                    [notifications].[notification_history_close_receipts_append_only];
                """);

            migrationBuilder.DropTable(
                name: "notification_history_close_receipts",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "user_notification_references",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_history_reference_states",
                schema: "notifications");
        }
    }
}
