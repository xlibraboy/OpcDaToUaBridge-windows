using OpcBridge.Client;
using OpcBridge.Core;

namespace OpcBridge.App.Hmi;

public static class HmiTagSnapshot
{
    public static HmiTagsResponse Build(MappingStore mappingStore, BridgeState bridgeState)
    {
        (IReadOnlyList<TagMapping> mappings, long version) = mappingStore.GetSnapshot();
        IReadOnlyList<BridgeValueSnapshot> values = bridgeState.GetValues();

        Dictionary<string, BridgeValueSnapshot> byKey = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Count; i++)
        {
            BridgeValueSnapshot v = values[i];
            byKey[string.Concat(v.SourceId, "::", v.DaItemId)] = v;
        }

        List<HmiTagDto> tags = new();
        for (int i = 0; i < mappings.Count; i++)
        {
            TagMapping m = mappings[i];
            if (!m.Enabled)
            {
                continue;
            }

            byKey.TryGetValue(string.Concat(m.SourceId, "::", m.DaItemId), out BridgeValueSnapshot? snap);
            tags.Add(new HmiTagDto
            {
                SourceId = m.SourceId,
                DaItemId = m.DaItemId,
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? m.DaItemId : m.DisplayName,
                DataType = m.DataType,
                Value = snap?.Value,
                TimestampUtc = snap?.TimestampUtc,
                DaQuality = snap?.DaQuality,
                IsGood = snap?.IsGood,
                Writeable = m.Writeable
            });
        }

        tags.Sort((a, b) =>
        {
            int c = string.Compare(a.SourceId, b.SourceId, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.DaItemId, b.DaItemId, StringComparison.OrdinalIgnoreCase);
        });

        return new HmiTagsResponse { Version = version, Tags = tags };
    }
}
