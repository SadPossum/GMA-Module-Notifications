namespace Gma.Modules.Notifications.Application;

using Gma.Framework.Naming;
using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Application.Ports;

internal sealed class NotificationDeliveryAdapterCatalog : INotificationDeliveryAdapterCatalog
{
    private readonly Dictionary<string, IUserNotificationSink> providers;
    private readonly Dictionary<string, IReadOnlyList<string>> providersByTag;

    public NotificationDeliveryAdapterCatalog(IEnumerable<IUserNotificationSink> sinks)
    {
        IUserNotificationSink[] registered = sinks
            .Where(sink => sink.DeliveryModes.HasFlag(NotificationSinkDeliveryMode.Durable))
            .ToArray();
        Dictionary<string, IUserNotificationSink> byProvider = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> byTag = new(StringComparer.Ordinal);

        foreach (IUserNotificationSink sink in registered)
        {
            string provider = SharedNameSegments.NormalizeKebabSegment(
                sink.ProviderName,
                "notification provider",
                nameof(sinks));
            if (!byProvider.TryAdd(provider, sink))
            {
                throw new InvalidOperationException($"Notification provider '{provider}' is registered more than once.");
            }

            string[] deliveryTags = NotificationTags.GetDeliveryTags(sink.DeliveryTags).ToArray();
            if (deliveryTags.Length == 0)
            {
                throw new InvalidOperationException($"Notification provider '{provider}' declares no delivery tags.");
            }

            foreach (string tag in deliveryTags)
            {
                if (!byTag.TryGetValue(tag, out List<string>? providers))
                {
                    providers = [];
                    byTag.Add(tag, providers);
                }

                providers.Add(provider);
            }
        }

        this.providers = byProvider;
        this.providersByTag = byTag.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.Order(StringComparer.Ordinal).ToArray()),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<string> GetProviders(string deliveryTag)
    {
        string normalized;
        try
        {
            normalized = NotificationTags.Normalize(deliveryTag);
        }
        catch (ArgumentException)
        {
            return [];
        }

        return this.providersByTag.TryGetValue(normalized, out IReadOnlyList<string>? providers)
            ? providers
            : [];
    }

    public bool Supports(string provider, string deliveryTag)
    {
        string normalizedProvider;
        try
        {
            normalizedProvider = SharedNameSegments.NormalizeKebabSegment(
                provider,
                "notification provider",
                nameof(provider));
        }
        catch (ArgumentException)
        {
            return false;
        }

        return this.providers.ContainsKey(normalizedProvider) &&
               this.GetProviders(deliveryTag).Contains(normalizedProvider, StringComparer.Ordinal);
    }

    public IUserNotificationSink? GetProvider(string provider)
    {
        string normalizedProvider;
        try
        {
            normalizedProvider = SharedNameSegments.NormalizeKebabSegment(
                provider,
                "notification provider",
                nameof(provider));
        }
        catch (ArgumentException)
        {
            return null;
        }

        return this.providers.GetValueOrDefault(normalizedProvider);
    }
}
