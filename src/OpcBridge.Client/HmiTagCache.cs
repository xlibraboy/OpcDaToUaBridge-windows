namespace OpcBridge.Client;

public sealed class HmiTagCache
{
    private readonly Dictionary<string, HmiTagDto> tags_ = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<HmiTagDto> Tags => tags_.Values;

    public void ReplaceAll(IEnumerable<HmiTagDto> tags)
    {
        tags_.Clear();
        foreach (HmiTagDto tag in tags)
        {
            tags_[Key(tag.SourceId, tag.DaItemId)] = tag;
        }
    }

    public void ApplyDeltas(IEnumerable<HmiValueDelta> deltas)
    {
        foreach (HmiValueDelta d in deltas)
        {
            if (!tags_.TryGetValue(Key(d.SourceId, d.DaItemId), out HmiTagDto? tag))
            {
                continue;
            }

            tag.Value = d.Value;
            tag.TimestampUtc = d.TimestampUtc;
            tag.DaQuality = d.DaQuality;
            tag.IsGood = d.IsGood;
        }
    }

    public static string Key(string sourceId, string daItemId) => string.Concat(sourceId, "::", daItemId);
}
