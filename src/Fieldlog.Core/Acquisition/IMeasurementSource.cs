using Fieldlog.Core.Channels;

namespace Fieldlog.Core.Acquisition;

/// <summary>
/// A provider of live measurement values — either a real OPC UA session or the built-in simulator.
/// </summary>
public interface IMeasurementSource : IAsyncDisposable
{
    /// <summary>True while a usable session exists.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Opens the source. Implementations must be idempotent: opening an already open source is a no-op.
    /// </summary>
    /// <exception cref="SourceUnavailableException">The endpoint could not be reached.</exception>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the source and releases the session. Safe to call on a source that was never opened.
    /// </summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Samples one channel. Never throws for a failed read — a <see cref="ReadingQuality.Bad"/>
    /// reading is returned instead, so a flaky server cannot take down the acquisition loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">The source is not open.</exception>
    Task<Reading> SampleAsync(ChannelDefinition channel, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when a measurement source cannot establish its session.
/// </summary>
public sealed class SourceUnavailableException : Exception
{
    public SourceUnavailableException(string message) : base(message) { }

    public SourceUnavailableException(string message, Exception inner) : base(message, inner) { }
}
