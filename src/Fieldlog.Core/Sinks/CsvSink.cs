using System.Globalization;
using System.Text;
using Fieldlog.Core.Acquisition;

namespace Fieldlog.Core.Sinks;

/// <summary>
/// Appends readings to a UTF-8 CSV file, one row per sample.
/// </summary>
/// <remarks>
/// Layout: <c>timestamp_utc,channel,value,quality</c> with ISO-8601 timestamps and invariant
/// number formatting, so the files import cleanly regardless of the operator's locale.
/// Rows are buffered and written in batches; <see cref="FlushAsync"/> and the periodic flush
/// interval bound the amount of data that could be lost on a crash.
/// </remarks>
public sealed class CsvSink : IReadingSink
{
    private const string Header = "timestamp_utc,channel,value,quality";

    private readonly string _path;
    private readonly int _batchSize;
    private readonly List<Reading> _buffer = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Creates a sink writing to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Target file; created (with header) if it does not exist.</param>
    /// <param name="batchSize">Number of buffered rows that triggers an automatic write-out.</param>
    public CsvSink(string path, int batchSize = 50)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _batchSize = Math.Max(1, batchSize);
    }

    /// <inheritdoc />
    public string Name => "csv";

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
            await File.WriteAllTextAsync(_path, Header + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AcceptAsync(Reading reading, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _buffer.Add(reading);
            if (_buffer.Count >= _batchSize)
                await WriteBufferAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteBufferAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task WriteBufferAsync(CancellationToken cancellationToken)
    {
        if (_buffer.Count == 0)
            return;

        var block = new StringBuilder();
        foreach (var reading in _buffer)
            block.AppendLine(FormatRow(reading));

        await File.AppendAllTextAsync(_path, block.ToString(), cancellationToken).ConfigureAwait(false);
        _buffer.Clear();
    }

    internal static string FormatRow(Reading reading) => string.Create(CultureInfo.InvariantCulture,
        $"{reading.TakenAtUtc:yyyy-MM-dd'T'HH:mm:ss.fff'Z'},{reading.ChannelKey},{reading.Value:R},{reading.Quality.ToString().ToLowerInvariant()}");
}
