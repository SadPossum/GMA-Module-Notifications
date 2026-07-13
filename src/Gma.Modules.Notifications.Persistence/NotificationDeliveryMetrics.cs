namespace Gma.Modules.Notifications.Persistence;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Gma.Framework.Observability;
using Gma.Framework.Observability.Infrastructure;
using Gma.Framework.Runtime;
using Microsoft.Extensions.Options;

internal sealed class NotificationDeliveryMetrics
{
    private readonly Counter<long> attempts;
    private readonly Histogram<double> duration;
    private NotificationDeliveryBacklogSnapshot backlog = new(0, 0, TimeSpan.Zero);

    public NotificationDeliveryMetrics(
        IMeterFactory meterFactory,
        IOptions<ApplicationIdentityOptions> applicationIdentity)
    {
        string applicationNamespace = applicationIdentity.Value.EffectiveNamespace;
        Meter meter = meterFactory.Create(ObservabilityMeterNames.NotificationsFor(applicationNamespace));
        this.attempts = meter.CreateCounter<long>(
            ObservabilityInstrumentNames.NotificationsDurableDeliveryAttemptsFor(applicationNamespace),
            description: "Number of durable notification delivery attempts by provider and outcome.");
        this.duration = meter.CreateHistogram<double>(
            ObservabilityInstrumentNames.NotificationsDurableDeliveryDurationFor(applicationNamespace),
            unit: "ms",
            description: "Duration of durable notification delivery adapter attempts.");
        meter.CreateObservableGauge(
            ObservabilityInstrumentNames.NotificationsDurableDeliveryBacklogFor(applicationNamespace),
            () => this.backlog.PendingCount,
            unit: "{delivery}",
            description: "Number of durable notification deliveries waiting for processing.");
        meter.CreateObservableGauge(
            ObservabilityInstrumentNames.NotificationsDurableDeliveryExhaustedFor(applicationNamespace),
            () => this.backlog.ExhaustedCount,
            unit: "{delivery}",
            description: "Number of durable notification deliveries that exhausted retries.");
        meter.CreateObservableGauge(
            ObservabilityInstrumentNames.NotificationsDurableDeliveryOldestPendingAgeFor(applicationNamespace),
            () => this.backlog.OldestPendingAge.TotalSeconds,
            unit: "s",
            description: "Age in seconds of the oldest durable notification delivery waiting for processing.");
    }

    public void RecordAttempt(string provider, string outcome, TimeSpan elapsed)
    {
        TagList tags = new()
        {
            { ObservabilityTagNames.Module, "notifications" },
            { ObservabilityTagNames.Provider, MetricTagValues.Provider(provider) },
            { ObservabilityTagNames.Result, MetricTagValues.Result(outcome) }
        };
        this.attempts.Add(1, tags);
        this.duration.Record(elapsed.TotalMilliseconds, tags);
    }

    public void RecordBacklog(NotificationDeliveryBacklogSnapshot snapshot) =>
        this.backlog = snapshot;
}

internal sealed record NotificationDeliveryBacklogSnapshot(
    long PendingCount,
    long ExhaustedCount,
    TimeSpan OldestPendingAge);
