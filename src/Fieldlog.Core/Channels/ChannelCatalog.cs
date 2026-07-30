using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fieldlog.Core.Channels;

/// <summary>
/// Loads and validates the channel catalog from a JSON document.
/// </summary>
/// <remarks>
/// The catalog is the single source of truth for which measurement points exist. Editing the
/// JSON file is enough to point the application at a different machine — no recompilation.
/// </remarks>
public sealed class ChannelCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, ChannelDefinition> _channels;

    private ChannelCatalog(IEnumerable<ChannelDefinition> channels)
    {
        _channels = new Dictionary<string, ChannelDefinition>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            Validate(channel);
            if (!_channels.TryAdd(channel.Key, channel))
                throw new CatalogFormatException($"Duplicate channel key '{channel.Key}'.");
        }

        if (_channels.Count == 0)
            throw new CatalogFormatException("The catalog contains no channels.");
    }

    /// <summary>All channels in declaration order.</summary>
    public IReadOnlyCollection<ChannelDefinition> Channels => _channels.Values;

    /// <summary>Number of declared channels.</summary>
    public int Count => _channels.Count;

    /// <summary>
    /// Parses a catalog from raw JSON text.
    /// </summary>
    /// <exception cref="CatalogFormatException">The document is malformed or fails validation.</exception>
    public static ChannelCatalog Parse(string json)
    {
        CatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new CatalogFormatException("The catalog is not valid JSON.", ex);
        }

        if (document?.Channels is not { Count: > 0 })
            throw new CatalogFormatException("The catalog must declare at least one channel.");

        return new ChannelCatalog(document.Channels);
    }

    /// <summary>
    /// Reads and parses a catalog file from disk.
    /// </summary>
    public static ChannelCatalog Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Looks up a channel by its key.
    /// </summary>
    public bool TryGet(string key, out ChannelDefinition channel)
    {
        if (_channels.TryGetValue(key, out var found))
        {
            channel = found;
            return true;
        }

        channel = null!;
        return false;
    }

    /// <summary>
    /// Returns the channel for <paramref name="key"/> or throws if it is unknown.
    /// </summary>
    /// <exception cref="CatalogFormatException">No channel with that key exists.</exception>
    public ChannelDefinition Require(string key) =>
        TryGet(key, out var channel)
            ? channel
            : throw new CatalogFormatException($"Unknown channel key '{key}'.");

    private static void Validate(ChannelDefinition channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Key))
            throw new CatalogFormatException("Every channel needs a non-empty 'key'.");

        if (string.IsNullOrWhiteSpace(channel.NodeId))
            throw new CatalogFormatException($"Channel '{channel.Key}' has no 'nodeId'.");

        if (channel.MinimumIntervalSeconds < 1)
            throw new CatalogFormatException($"Channel '{channel.Key}': minimum interval must be at least one second.");

        if (channel.MaximumIntervalSeconds < channel.MinimumIntervalSeconds)
            throw new CatalogFormatException($"Channel '{channel.Key}': maximum interval is below the minimum.");

        if (channel.DefaultIntervalSeconds < channel.MinimumIntervalSeconds ||
            channel.DefaultIntervalSeconds > channel.MaximumIntervalSeconds)
            throw new CatalogFormatException($"Channel '{channel.Key}': default interval is outside the allowed range.");

        if (channel.Simulation is { } sim && sim.Ceiling <= sim.Floor)
            throw new CatalogFormatException($"Channel '{channel.Key}': simulation ceiling must exceed the floor.");
    }

    private sealed class CatalogDocument
    {
        public List<ChannelDefinition>? Channels { get; set; }
    }
}

/// <summary>
/// Raised when the channel catalog cannot be parsed or fails validation.
/// </summary>
public sealed class CatalogFormatException : Exception
{
    public CatalogFormatException(string message) : base(message) { }

    public CatalogFormatException(string message, Exception inner) : base(message, inner) { }
}
