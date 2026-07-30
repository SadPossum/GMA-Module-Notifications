using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.PostgreSqlMigrations.Migrations
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
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ResultingVersion = table.Column<long>(type: "bigint", nullable: false),
                    RemovedRecordCount = table.Column<int>(type: "integer", nullable: false),
                    RemovedRecordIdsSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CloseOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseRequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true)
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
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Namespace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notification_references", x => new { x.ScopeId, x.NotificationId, x.Namespace, x.Digest });
                    table.ForeignKey(
                        name: "FK_user_notification_references_notification_history_reference~",
                        columns: x => new { x.ScopeId, x.Namespace, x.Digest },
                        principalSchema: "notifications",
                        principalTable: "notification_history_reference_states",
                        principalColumns: new[] { "ScopeId", "Namespace", "Digest" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_notification_references_user_notifications_Notificatio~",
                        column: x => x.NotificationId,
                        principalSchema: "notifications",
                        principalTable: "user_notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_close_receipts_ScopeId_Namespace_Diges~",
                schema: "notifications",
                table: "notification_history_close_receipts",
                columns: new[] { "ScopeId", "Namespace", "Digest", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_reference_states_ScopeId_IsClosed_Clos~",
                schema: "notifications",
                table: "notification_history_reference_states",
                columns: new[] { "ScopeId", "IsClosed", "ClosedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_notification_references_NotificationId",
                schema: "notifications",
                table: "user_notification_references",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_notification_references_ScopeId_Namespace_Digest_Notif~",
                schema: "notifications",
                table: "user_notification_references",
                columns: new[] { "ScopeId", "Namespace", "Digest", "NotificationId" });

            migrationBuilder.Sql(
                """
                INSERT INTO notifications.notification_history_reference_states
                    ("ScopeId", "Namespace", "Digest", "Version", "IsClosed")
                SELECT
                    notification."ScopeId",
                    'recipient',
                    encode(
                        sha256(
                            convert_to(
                                'gma-notification-recipient/v1|' ||
                                notification."ScopeId" ||
                                '|' ||
                                notification."UserId",
                                'UTF8')),
                        'hex'),
                    COUNT(*)::bigint,
                    FALSE
                FROM notifications.user_notifications AS notification
                GROUP BY
                    notification."ScopeId",
                    notification."UserId"
                ON CONFLICT ("ScopeId", "Namespace", "Digest") DO NOTHING;

                INSERT INTO notifications.user_notification_references
                    ("NotificationId", "ScopeId", "Namespace", "Digest")
                SELECT
                    notification."Id",
                    notification."ScopeId",
                    'recipient',
                    encode(
                        sha256(
                            convert_to(
                                'gma-notification-recipient/v1|' ||
                                notification."ScopeId" ||
                                '|' ||
                                notification."UserId",
                                'UTF8')),
                        'hex')
                FROM notifications.user_notifications AS notification
                ON CONFLICT
                    ("ScopeId", "NotificationId", "Namespace", "Digest")
                    DO NOTHING;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION notifications.reject_notification_history_close_receipt_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'notification history close receipts are append-only';
                END;
                $$;

                CREATE TRIGGER notification_history_close_receipts_append_only
                BEFORE UPDATE OR DELETE
                ON notifications.notification_history_close_receipts
                FOR EACH ROW
                EXECUTE FUNCTION notifications.reject_notification_history_close_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS notification_history_close_receipts_append_only
                ON notifications.notification_history_close_receipts;

                DROP FUNCTION IF EXISTS
                    notifications.reject_notification_history_close_receipt_mutation();
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
