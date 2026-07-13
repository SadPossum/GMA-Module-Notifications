namespace Gma.Modules.Notifications.Contracts;

using Gma.Framework.Messaging;

public static class NotificationsIntegrationSubjects
{
    public static string CreateUserNotificationRequested(
        string producerModule,
        string subjectPrefix = IntegrationEventNaming.DefaultSubjectPrefix) =>
        IntegrationEventNaming.CreateSubject(
            subjectPrefix,
            producerModule,
            UserNotificationRequestedIntegrationEvent.EventType,
            UserNotificationRequestedIntegrationEvent.EventVersion);

    public static string CreateUserNotificationRequestedV2(
        string producerModule,
        string subjectPrefix = IntegrationEventNaming.DefaultSubjectPrefix) =>
        IntegrationEventNaming.CreateSubject(
            subjectPrefix,
            producerModule,
            UserNotificationRequestedIntegrationEventV2.EventType,
            UserNotificationRequestedIntegrationEventV2.EventVersion);
}
