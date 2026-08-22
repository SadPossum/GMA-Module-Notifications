using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.Notifications.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class EnforceNotificationScopeStateStorageGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION
                    notifications.reject_closed_notification_scope_state_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Version" <> 1 THEN
                            RAISE EXCEPTION
                                'notification scope state version must begin at one';
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'notification scope state cannot be deleted';
                    END IF;

                    IF OLD."IsClosed" THEN
                        RAISE EXCEPTION
                            'closed notification scope state is immutable';
                    END IF;

                    IF NEW."ScopeId" IS DISTINCT FROM OLD."ScopeId" THEN
                        RAISE EXCEPTION
                            'notification scope state identity is immutable';
                    END IF;

                    IF NEW."Version"::numeric - OLD."Version"::numeric <> 1 THEN
                        RAISE EXCEPTION
                            'notification scope state version must advance by exactly one';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                DROP TRIGGER notification_scope_states_closed_immutable
                ON notifications.notification_scope_states;

                CREATE TRIGGER notification_scope_states_closed_immutable
                BEFORE INSERT OR UPDATE OR DELETE
                ON notifications.notification_scope_states
                FOR EACH ROW
                EXECUTE FUNCTION
                    notifications.reject_closed_notification_scope_state_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION
                    notifications.reject_closed_notification_scope_state_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."IsClosed" THEN
                        RAISE EXCEPTION
                            'closed notification scope state is immutable';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$;

                DROP TRIGGER notification_scope_states_closed_immutable
                ON notifications.notification_scope_states;

                CREATE TRIGGER notification_scope_states_closed_immutable
                BEFORE UPDATE OR DELETE
                ON notifications.notification_scope_states
                FOR EACH ROW
                EXECUTE FUNCTION
                    notifications.reject_closed_notification_scope_state_mutation();
                """);
        }
    }
}
