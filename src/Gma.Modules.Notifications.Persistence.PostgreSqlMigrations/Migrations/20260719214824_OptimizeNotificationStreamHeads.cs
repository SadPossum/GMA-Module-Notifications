using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeNotificationStreamHeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_StreamSequence",
                schema: "notifications",
                table: "user_notifications",
                column: "StreamSequence");

            migrationBuilder.CreateIndex(
                name: "IX_notification_broadcasts_StreamSequence",
                schema: "notifications",
                table: "notification_broadcasts",
                column: "StreamSequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_notifications_StreamSequence",
                schema: "notifications",
                table: "user_notifications");

            migrationBuilder.DropIndex(
                name: "IX_notification_broadcasts_StreamSequence",
                schema: "notifications",
                table: "notification_broadcasts");
        }
    }
}
