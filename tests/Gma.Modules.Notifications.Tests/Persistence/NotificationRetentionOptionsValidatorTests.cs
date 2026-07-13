namespace Gma.Modules.Notifications.Tests.Persistence;

using Gma.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class NotificationRetentionOptionsValidatorTests
{
    [Fact]
    public void Validate_accepts_enabled_defaults()
    {
        NotificationRetentionOptions options = new()
        {
            Enabled = true,
        };

        ValidateOptionsResult result = new NotificationRetentionOptionsValidator()
            .Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_001)]
    public void Validate_rejects_out_of_range_maximum_batches(int maximumBatches)
    {
        NotificationRetentionOptions options = new()
        {
            Enabled = true,
            MaxBatchesPerCategoryPerCycle = maximumBatches,
        };

        ValidateOptionsResult result = new NotificationRetentionOptionsValidator()
            .Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "Notifications:Retention:MaxBatchesPerCategoryPerCycle",
            result.FailureMessage,
            StringComparison.Ordinal);
    }
}
