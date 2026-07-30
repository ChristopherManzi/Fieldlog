using Fieldlog.Core.Acquisition;

namespace Fieldlog.Core.Sinks;

/// <summary>
/// A destination for readings — a file, a database, or anything else that stores samples.
/// </summary>
public interface IReadingSink : IAsyncDisposable
{
    /// <summary>Short human-readable name used in error reporting.</summary>
    string Name { get; }

    /// <summary>
    /// Called once before acquisition starts; the sink allocates resources here
    /// (opens the file, verifies the database schema, …).
    /// </summary>
    Task PrepareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores one reading. May buffer internally; <see cref="FlushAsync"/> forces persistence.
    /// </summary>
    Task AcceptAsync(Reading reading, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists any buffered readings.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
