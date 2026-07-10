namespace Gma.Modules.Notifications.Application.Ports;

using Gma.Modules.Notifications.Contracts;

public interface INotificationPreferenceEvaluator
{
    ValueTask<bool> ShouldStoreAsync(
        NotificationPreferenceRequest request,
        CancellationToken cancellationToken);
}

public sealed record NotificationPreferenceRequest(
    string ScopeId,
    string UserId,
    string SourceModule,
    string NotificationName,
    int NotificationVersion,
    NotificationSeverity Severity);

internal sealed class AllowAllNotificationPreferenceEvaluator : INotificationPreferenceEvaluator
{
    public ValueTask<bool> ShouldStoreAsync(
        NotificationPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }
}
