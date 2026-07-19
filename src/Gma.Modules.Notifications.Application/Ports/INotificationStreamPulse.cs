namespace Gma.Modules.Notifications.Application.Ports;

public interface INotificationStreamPulse
{
    long CaptureVersion(NotificationStreamKind streamKind);

    ValueTask<bool> WaitForChangeAsync(
        NotificationStreamKind streamKind,
        long observedVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
