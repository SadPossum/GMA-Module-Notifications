namespace Gma.Modules.Notifications.Persistence;

using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Ports;

internal sealed class NotificationStreamPulse : INotificationStreamPulse
{
    private readonly StreamState history = new();
    private readonly StreamState broadcasts = new();

    public long CaptureVersion(NotificationStreamKind streamKind) =>
        this.State(streamKind).CaptureVersion();

    public ValueTask<bool> WaitForChangeAsync(
        NotificationStreamKind streamKind,
        long observedVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        this.State(streamKind).WaitForChangeAsync(observedVersion, timeout, cancellationToken);

    public void Advance(NotificationStreamKind streamKind, long version) =>
        this.State(streamKind).Advance(version);

    private StreamState State(NotificationStreamKind streamKind) => streamKind switch
    {
        NotificationStreamKind.History => this.history,
        NotificationStreamKind.Broadcasts => this.broadcasts,
        _ => throw new ArgumentOutOfRangeException(nameof(streamKind), streamKind, "Notification stream kind is invalid.")
    };

    private sealed class StreamState
    {
        private readonly Lock sync = new();
        private long version;
        private TaskCompletionSource change = NewSignal();

        public long CaptureVersion() => Interlocked.Read(ref this.version);

        public void Advance(long nextVersion)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(nextVersion);

            TaskCompletionSource? completedSignal = null;
            lock (this.sync)
            {
                if (nextVersion <= this.version)
                {
                    return;
                }

                Interlocked.Exchange(ref this.version, nextVersion);
                completedSignal = this.change;
                this.change = NewSignal();
            }

            completedSignal.TrySetResult();
        }

        public async ValueTask<bool> WaitForChangeAsync(
            long observedVersion,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(observedVersion, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

            Task signal;
            lock (this.sync)
            {
                if (this.version != observedVersion)
                {
                    return true;
                }

                signal = this.change.Task;
            }

            try
            {
                await signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
