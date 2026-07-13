namespace Gma.Modules.Notifications.Integrations.Auth;

using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Contracts;

internal static class AuthNotificationTags
{
    private static readonly NotificationTag Email = new(
        NotificationTags.Email,
        NotificationTagKind.Delivery,
        "Email",
        "Deliver through the configured email pipeline.");

    private static readonly NotificationTag Web = new(
        NotificationTags.Web,
        NotificationTagKind.Delivery,
        "Web",
        "Show in web notification feeds.");

    private static readonly NotificationTag Security = new(
        "domain:security",
        NotificationTagKind.Domain,
        "Security",
        "Account security and authentication activity.");

    private static readonly NotificationTag Authentication = new(
        "domain:authentication",
        NotificationTagKind.Domain,
        "Authentication",
        "Authentication and session activity.");

    private static readonly NotificationTag EmailVerification = new(
        "domain:email-verification",
        NotificationTagKind.Domain,
        "Email verification",
        "Email ownership verification activity.");

    public static IReadOnlyList<NotificationTag> SignIn { get; } =
        [Email, Web, Security, Authentication];

    public static IReadOnlyList<NotificationTag> AuthenticationMethodChanged { get; } =
        [Email, Web, Security, Authentication];

    public static IReadOnlyList<NotificationTag> VerificationRequest { get; } =
        [Email, Security, EmailVerification];

    public static IReadOnlyList<NotificationTag> VerificationCompleted { get; } =
        [Web, Security, EmailVerification];
}
