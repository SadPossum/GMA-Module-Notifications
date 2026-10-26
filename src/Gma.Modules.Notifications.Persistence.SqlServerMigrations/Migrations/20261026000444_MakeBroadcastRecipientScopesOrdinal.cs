using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class MakeBroadcastRecipientScopesOrdinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecipientScope",
                schema: "notifications",
                table: "notification_broadcast_reads",
                type: "nvarchar(135)",
                maxLength: 135,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(135)",
                oldMaxLength: 135);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecipientScope",
                schema: "notifications",
                table: "notification_broadcast_reads",
                type: "nvarchar(135)",
                maxLength: 135,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(135)",
                oldMaxLength: 135,
                oldCollation: "Latin1_General_100_BIN2");
        }
    }
}
