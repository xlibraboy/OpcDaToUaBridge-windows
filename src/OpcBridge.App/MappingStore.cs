using System.Text.Json;
using Microsoft.Extensions.Options;
using OpcBridge.Core;

namespace OpcBridge.App;

/// <summary>
/// Holds the live tag mappings. Changes bump a version that BridgeWorker watches,
/// and are persisted to mappings.json so they survive restarts.
/// </summary>
public sealed class MappingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object sync_ = new();
    private readonly string persist_path_;
    private List<TagMapping> mappings_;
    private long version_;

    public MappingStore(IOptions<BridgeOptions> options)
    {
        persist_path_ = Path.Combine(AppContext.BaseDirectory, "mappings.json");
        mappings_ = LoadFromDisk() ?? options.Value.Mappings ?? new List<TagMapping>();
    }

    public (IReadOnlyList<TagMapping> Mappings, long Version) GetSnapshot()
    {
        lock (sync_)
        {
            return (mappings_.ToArray(), version_);
        }
    }

    public long Version
    {
        get { lock (sync_) { return version_; } }
    }

    /// <summary>Add one or more tags. Skips duplicates by DA item id. Returns the new version.</summary>
    public long Add(IEnumerable<TagMapping> tags)
    {
        lock (sync_)
        {
            bool changed = false;
            foreach (TagMapping tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag.DaItemId)) continue;
                if (mappings_.Any(m => string.Equals(m.DaItemId, tag.DaItemId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                mappings_.Add(Normalize(tag));
                changed = true;
            }

            if (changed) { version_++; Persist(); }
            return version_;
        }
    }

    /// <summary>Remove a tag by DA item id. Returns the new version.</summary>
    public long Remove(string daItemId)
    {
        lock (sync_)
        {
            int removed = mappings_.RemoveAll(m =>
                string.Equals(m.DaItemId, daItemId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) { version_++; Persist(); }
            return version_;
        }
    }

    /// <summary>Replace the entire mapping list.</summary>
    public long SetAll(IEnumerable<TagMapping> tags)
    {
        lock (sync_)
        {
            mappings_ = tags.Where(t => !string.IsNullOrWhiteSpace(t.DaItemId)).Select(Normalize).ToList();
            version_++;
            Persist();
            return version_;
        }
    }

    private static TagMapping Normalize(TagMapping tag)
    {
        string itemId = tag.DaItemId.Trim();
        return new TagMapping
        {
            DaItemId = itemId,
            UaNodeId = string.IsNullOrWhiteSpace(tag.UaNodeId) ? $"ns=2;s={itemId}" : tag.UaNodeId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(tag.DisplayName) ? itemId : tag.DisplayName.Trim(),
            DataType = string.IsNullOrWhiteSpace(tag.DataType) ? "Auto" : tag.DataType.Trim()
        };
    }

    private void Persist()
    {
        try
        {
            string json = JsonSerializer.Serialize(mappings_, JsonOptions);
            File.WriteAllText(persist_path_, json);
        }
        catch
        {
            // persistence is best-effort; in-memory state remains authoritative
        }
    }

    private List<TagMapping>? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(persist_path_)) return null;
            string json = File.ReadAllText(persist_path_);
            return JsonSerializer.Deserialize<List<TagMapping>>(json);
        }
        catch
        {
            return null;
        }
    }
}
