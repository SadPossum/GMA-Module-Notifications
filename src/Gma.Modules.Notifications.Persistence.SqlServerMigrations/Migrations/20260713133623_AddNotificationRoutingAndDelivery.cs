using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRoutingAndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryPolicy",
                schema: "notifications",
                table: "user_notifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "respect-preferences");

            migrationBuilder.AddColumn<bool>(
                name: "IsInboxVisible",
                schema: "notifications",
                table: "user_notifications",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryTag = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliveries_user_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalSchema: "notifications",
                        principalTable: "user_notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_routes",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryTag = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_routes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TagKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tag_definitions",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_notification_tags",
                schema: "notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TagKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notification_tags", x => new { x.ScopeId, x.NotificationId, x.TagKey });
                    table.ForeignKey(
                        name: "FK_user_notification_tags_user_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalSchema: "notifications",
                        principalTable: "user_notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalSchema: "notifications",
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_ScopeId_IsInboxVisible_CreatedAtUtc",
                schema: "notifications",
                table: "user_notifications",
                columns: new[] { "ScopeId", "IsInboxVisible", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_NotificationId",
                schema: "notifications",
                table: "deliveries",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_ScopeId_NotificationId_DeliveryTag_Provider",
                schema: "notifications",
                table: "deliveries",
                columns: new[] { "ScopeId", "NotificationId", "DeliveryTag", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_ScopeId_Status_CreatedAtUtc",
                schema: "notifications",
                table: "deliveries",
                columns: new[] { "ScopeId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_Status_NextAttemptAtUtc_LockedUntilUtc_CreatedAtUtc",
                schema: "notifications",
                table: "deliveries",
                columns: new[] { "Status", "NextAttemptAtUtc", "LockedUntilUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_DeliveryId",
                schema: "notifications",
                table: "delivery_attempts",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_ScopeId_CompletedAtUtc",
                schema: "notifications",
                table: "delivery_attempts",
                columns: new[] { "ScopeId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_ScopeId_DeliveryId_AttemptNumber",
                schema: "notifications",
                table: "delivery_attempts",
                columns: new[] { "ScopeId", "DeliveryId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_routes_ScopeId_DeliveryTag",
                schema: "notifications",
                table: "delivery_routes",
                columns: new[] { "ScopeId", "DeliveryTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_routes_ScopeId_Provider_IsActive",
                schema: "notifications",
                table: "delivery_routes",
                columns: new[] { "ScopeId", "Provider", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_preferences_ScopeId_UserId_Enabled",
                schema: "notifications",
                table: "preferences",
                columns: new[] { "ScopeId", "UserId", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_preferences_ScopeId_UserId_TagKey",
                schema: "notifications",
                table: "preferences",
                columns: new[] { "ScopeId", "UserId", "TagKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tag_definitions_ScopeId_Kind_IsActive_TagKey",
                schema: "notifications",
                table: "tag_definitions",
                columns: new[] { "ScopeId", "Kind", "IsActive", "TagKey" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_definitions_ScopeId_TagKey",
                schema: "notifications",
                table: "tag_definitions",
                columns: new[] { "ScopeId", "TagKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_notification_tags_NotificationId",
                schema: "notifications",
                table: "user_notification_tags",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_notification_tags_ScopeId_TagKey_NotificationId",
                schema: "notifications",
                table: "user_notification_tags",
                columns: new[] { "ScopeId", "TagKey", "NotificationId" });

            migrationBuilder.Sql(
                """
                INSERT INTO [notifications].[user_notification_tags] ([NotificationId], [ScopeId], [TagKey])
                SELECT [Id], [ScopeId], 'delivery:web'
                FROM [notifications].[user_notifications];
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE [notifications].[user_notifications]
                    ADD CONSTRAINT [CK_user_notifications_DeliveryPolicy]
                    CHECK ([DeliveryPolicy] IN ('respect-preferences', 'mandatory'));
                ALTER TABLE [notifications].[deliveries]
                    ADD CONSTRAINT [CK_deliveries_Attempts]
                    CHECK ([MaxAttempts] >= 1 AND [Attempts] >= 0 AND [Attempts] <= [MaxAttempts]),
                        CONSTRAINT [CK_deliveries_Status]
                    CHECK ([Status] IN ('pending', 'processing', 'retry-scheduled', 'delivered', 'rejected', 'exhausted', 'suppressed', 'unroutable')),
                        CONSTRAINT [CK_deliveries_Lease]
                    CHECK (([LockedBy] IS NULL AND [LockedUntilUtc] IS NULL) OR ([LockedBy] IS NOT NULL AND [LockedUntilUtc] IS NOT NULL));
                ALTER TABLE [notifications].[delivery_attempts]
                    ADD CONSTRAINT [CK_delivery_attempts_AttemptNumber] CHECK ([AttemptNumber] >= 1),
                        CONSTRAINT [CK_delivery_attempts_Timestamps] CHECK ([CompletedAtUtc] >= [StartedAtUtc]),
                        CONSTRAINT [CK_delivery_attempts_Outcome] CHECK ([Outcome] IN ('delivered', 'retry', 'rejected', 'exception'));
                ALTER TABLE [notifications].[preferences]
                    ADD CONSTRAINT [CK_preferences_Version] CHECK ([Version] >= 1);
                ALTER TABLE [notifications].[delivery_routes]
                    ADD CONSTRAINT [CK_delivery_routes_Version] CHECK ([Version] >= 1);
                ALTER TABLE [notifications].[tag_definitions]
                    ADD CONSTRAINT [CK_tag_definitions_Version] CHECK ([Version] >= 1),
                        CONSTRAINT [CK_tag_definitions_Kind] CHECK ([Kind] IN ('delivery', 'domain')),
                        CONSTRAINT [CK_tag_definitions_Origin] CHECK ([Origin] IN ('system', 'module', 'operator'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "delivery_routes",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "tag_definitions",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "user_notification_tags",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_user_notifications_ScopeId_IsInboxVisible_CreatedAtUtc",
                schema: "notifications",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "DeliveryPolicy",
                schema: "notifications",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "IsInboxVisible",
                schema: "notifications",
                table: "user_notifications");
        }
    }
}
