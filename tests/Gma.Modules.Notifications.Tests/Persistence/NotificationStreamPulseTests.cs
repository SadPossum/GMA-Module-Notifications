namespace Gma.Modules.Notifications.Tests;

using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Persistence;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationStreamPulseTests
{
    [Fact]
    public async Task Pulse_wakes_waiters_only_when_the_observed_stream_advances()
    {
        NotificationStreamPulse pulse = new();
        long observed = pulse.CaptureVersion(NotificationStreamKind.History);

        ValueTask<bool> waiting = pulse.WaitForChangeAsync(
            NotificationStreamKind.History,
            observed,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        pulse.Advance(NotificationStreamKind.Broadcasts, 3);
        Assert.False(waiting.IsCompleted);

        pulse.Advance(NotificationStreamKind.History, 4);

        Assert.True(await waiting);
        Assert.Equal(4, pulse.CaptureVersion(NotificationStreamKind.History));
    }

    [Fact]
    public async Task Pulse_timeout_supports_heartbeat_recovery()
    {
        NotificationStreamPulse pulse = new();

        bool changed = await pulse.WaitForChangeAsync(
            NotificationStreamKind.History,
            observedVersion: 0,
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(changed);
    }

    [Fact]
    public void Stream_options_reject_heartbeat_shorter_than_poll_interval()
    {
        NotificationStreamOptions options = new()
        {
            PollInterval = TimeSpan.FromSeconds(10),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        Microsoft.Extensions.Options.ValidateOptionsResult result =
            new NotificationStreamOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

}
