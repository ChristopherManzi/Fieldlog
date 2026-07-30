namespace Fieldlog.Core.Acquisition;

/// <summary>
/// Quality of a single sample.
/// </summary>
public enum ReadingQuality
{
    /// <summary>The source returned a trustworthy value.</summary>
    Good,

    /// <summary>The source responded, but flagged the value as unreliable.</summary>
    Uncertain,

    /// <summary>The read failed; <see cref="Reading.Value"/> is meaningless.</summary>
    Bad
}

/// <summary>
/// One timestamped sample taken from a channel.
/// </summary>
/// <param name="ChannelKey">Catalog key of the channel the sample belongs to.</param>
/// <param name="TakenAtUtc">UTC instant at which the sample was acquired.</param>
/// <param name="Value">Numeric value in engineering units.</param>
/// <param name="Quality">Reliability classification of the sample.</param>
public readonly record struct Reading(
    string ChannelKey,
    DateTimeOffset TakenAtUtc,
    double Value,
    ReadingQuality Quality)
{
    /// <summary>Convenience factory for a failed read.</summary>
    public static Reading Failed(string channelKey, DateTimeOffset takenAtUtc) =>
        new(channelKey, takenAtUtc, double.NaN, ReadingQuality.Bad);

    /// <summary>True when the sample can be persisted and displayed.</summary>
    public bool IsUsable => Quality != ReadingQuality.Bad && !double.IsNaN(Value);
}
