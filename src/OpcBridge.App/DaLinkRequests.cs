using Microsoft.Extensions.Logging;
using OpcBridge.Core;
using OpcBridge.Da;

namespace OpcBridge.App;

public sealed record DaLinkDto(
    Guid Id,
    string ProviderSourceId,
    string ProviderItemId,
    string ConsumerSourceId,
    string ConsumerItemId,
    bool Enabled,
    short? ProviderCanonicalType,
    short? ConsumerCanonicalType,
    int? ProviderAccessRights = null,
    int? ConsumerAccessRights = null);

public sealed record CreateDaLinkRequest(DaLinkDto? Link);

public sealed record UpdateDaLinkRequest(DaLinkDto? Link);

public sealed record DaTagMetadata(short? CanonicalType, int? AccessRights);

public interface IDaLinkMetadataResolver
{
    bool TryResolve(string sourceId, string itemId, out DaTagMetadata metadata);
}


internal static class DaLinkValidators
{
    public static string? Validate(
        DaLinkDto link,
        bool consumerHasProvider)
    {
        if (link.ProviderAccessRights is null)
        {
            return "Provider tag not found.";
        }

        if (link.ConsumerAccessRights is null)
        {
            return "Consumer tag not found.";
        }

        return Validate(
            link,
            consumerHasProvider,
            providerReadable: IsReadable(link.ProviderAccessRights.Value),
            consumerWritable: IsWritable(link.ConsumerAccessRights.Value));
    }

    public static string? Validate(
        DaLinkDto link,
        bool consumerHasProvider,
        bool providerReadable,
        bool consumerWritable)
    {
        if (string.IsNullOrWhiteSpace(link.ProviderItemId))
        {
            return "Provider item is required.";
        }

        if (string.IsNullOrWhiteSpace(link.ConsumerItemId))
        {
            return "Consumer item is required.";
        }

        if (string.Equals(NormalizeSourceId(link.ProviderSourceId), NormalizeSourceId(link.ConsumerSourceId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(link.ProviderItemId.Trim(), link.ConsumerItemId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Provider and consumer cannot be the same tag.";
        }

        if (!providerReadable)
        {
            return "Provider tag must allow read.";
        }

        if (!consumerWritable)
        {
            return "Consumer tag must allow write.";
        }

        if (consumerHasProvider)
        {
            return "Consumer already has a provider.";
        }

        if (link.ProviderCanonicalType != link.ConsumerCanonicalType)
        {
            return "Provider and consumer must use the same native OPC DA type.";
        }

        return null;
    }

    private static bool IsReadable(int accessRights)
    {
        return (accessRights & 1) != 0;
    }

    private static bool IsWritable(int accessRights)
    {
        return (accessRights & 2) != 0;
    }

    private static string NormalizeSourceId(string? sourceId)
    {
        string value = sourceId?.Trim() ?? string.Empty;
        return value.Length == 0 ? DaRuntimeSettings.DefaultSourceId : value;
    }
}

internal static class DaLinkApiHelpers
{
    public static bool TryMigrateLegacyDaLinks(
        DaLinkStore daLinkStore,
        IReadOnlyList<TagMapping> legacyMappings,
        DashboardLogStore logStore,
        ILogger logger,
        out string? warning)
    {
        ArgumentNullException.ThrowIfNull(daLinkStore);
        ArgumentNullException.ThrowIfNull(legacyMappings);
        ArgumentNullException.ThrowIfNull(logStore);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            daLinkStore.MigrateFromMappings(legacyMappings);
            warning = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            warning = $"Skipping legacy DA link migration from mappings.json because {ex.Message}";
            logStore.Add(LogLevel.Warning, "OpcBridge.App.DaLinkMigration", warning, ex);
            logger.LogWarning(ex, "Skipping legacy DA link migration from mappings.json because {Reason}", ex.Message);
            return false;
        }
    }

    public static bool TryGetStoredDaLinkRule(DaLinkStore linkStore, Guid id, out DaLinkRule? rule)
    {
        ArgumentNullException.ThrowIfNull(linkStore);

        (IReadOnlyList<DaLinkRule> rules, _) = linkStore.GetSnapshot();
        rule = rules.FirstOrDefault(existing => existing.Id == id);
        return rule is not null;
    }
}
