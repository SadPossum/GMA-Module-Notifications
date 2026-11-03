using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class EnforceNotificationScopeStateStorageGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TRIGGER
                    [notifications].[notification_scope_states_closed_immutable]
                ON [notifications].[notification_scope_states]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF EXISTS (SELECT 1 FROM deleted)
                       AND NOT EXISTS (SELECT 1 FROM inserted)
                    BEGIN
                        THROW 51000,
                            'notification scope state cannot be deleted',
                            1;
                    END;

                    IF EXISTS (SELECT 1 FROM deleted)
                       AND EXISTS (SELECT 1 FROM inserted)
                       AND (
                           UPDATE([ScopeId])
                           OR EXISTS (
                               SELECT [ScopeId]
                               FROM deleted
                               EXCEPT
                               SELECT [ScopeId]
                               FROM inserted))
                    BEGIN
                        THROW 51000,
                            'notification scope state identity is immutable',
                            1;
                    END;

                    IF EXISTS (
                        SELECT 1
                        FROM deleted AS previous_state
                        INNER JOIN inserted AS next_state
                            ON next_state.[ScopeId] = previous_state.[ScopeId]
                        WHERE previous_state.[IsClosed] = 1)
                    BEGIN
                        THROW 51000,
                            'closed notification scope state is immutable',
                            1;
                    END;

                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS next_state
                        LEFT JOIN deleted AS previous_state
                            ON previous_state.[ScopeId] = next_state.[ScopeId]
                        WHERE previous_state.[ScopeId] IS NULL
                          AND next_state.[Version] <> 1)
                    BEGIN
                        THROW 51000,
                            'notification scope state version must begin at one',
                            1;
                    END;

                    IF EXISTS (
                        SELECT 1
                        FROM deleted AS previous_state
                        INNER JOIN inserted AS next_state
                            ON next_state.[ScopeId] = previous_state.[ScopeId]
                        WHERE CAST(next_state.[Version] AS decimal(20, 0)) -
                              CAST(previous_state.[Version] AS decimal(20, 0)) <> 1)
                    BEGIN
                        THROW 51000,
                            'notification scope state version must advance by exactly one',
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
                ALTER TRIGGER
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
        }
    }
}
