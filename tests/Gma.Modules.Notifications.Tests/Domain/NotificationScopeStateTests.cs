namespace Gma.Modules.Notifications.Tests;

using Gma.Modules.Notifications.Domain.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationScopeStateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mutation_versions_are_monotonic_until_scope_close()
    {
        NotificationScopeState state =
            NotificationScopeState.Create("tenant-a").Value;

        Assert.True(state.RegisterMutation());
        Assert.True(state.RegisterMutation());
        Assert.Equal(2, state.Version);

        NotificationScopeCloseTransition closed = state.Close(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new string('a', 64),
            Now);

        Assert.Equal(NotificationScopeCloseTransition.Completed, closed);
        Assert.Equal(3, state.Version);
        Assert.True(state.IsClosed);
        Assert.False(state.RegisterMutation());
    }

    [Fact]
    public void Close_replays_only_the_exact_operation_and_request()
    {
        Guid operationId =
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        NotificationScopeState state =
            NotificationScopeState.Create("tenant-a").Value;
        Assert.Equal(
            NotificationScopeCloseTransition.Completed,
            state.Close(operationId, new string('a', 64), Now));

        Assert.Equal(
            NotificationScopeCloseTransition.Replayed,
            state.Close(operationId, new string('a', 64), Now.AddMinutes(1)));
        Assert.Equal(
            NotificationScopeCloseTransition.Conflict,
            state.Close(operationId, new string('b', 64), Now.AddMinutes(1)));
        Assert.Equal(
            NotificationScopeCloseTransition.Conflict,
            state.Close(Guid.NewGuid(), new string('a', 64), Now.AddMinutes(1)));
    }
}
