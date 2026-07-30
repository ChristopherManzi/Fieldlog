using Npgsql;
using Fieldlog.Core.Acquisition;

namespace Fieldlog.Core.Sinks;

/// <summary>
/// Persists readings into a PostgreSQL table using batched inserts.
/// </summary>
/// <remarks>
/// The sink writes into <c>measurement(channel_key, taken_at, reading, quality)</c> — see
/// <c>deploy/initdb/schema.sql</c>. Channels are referenced by their catalog key directly;
/// no numeric surrogate lookup is needed on the hot path.
/// </remarks>
public sealed class PostgresSink : IReadingSink
{
    private const string InsertStatement =
        "INSERT INTO measurement (channel_key, taken_at, reading, quality) VALUES ($1, $2, $3, $4)";

    private readonly NpgsqlDataSource _dataSource;
    private readonly int _batchSize;
    private readonly List<Reading> _pending = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Creates a sink for the given connection string.
    /// </summary>
    /// <param name="connectionString">Npgsql connection string.</param>
    /// <param name="batchSize">Pending readings that trigger an automatic batch insert.</param>
    public PostgresSink(string connectionString, int batchSize = 20)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _batchSize = Math.Max(1, batchSize);
    }

    /// <inheritdoc />
    public string Name => "postgres";

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        // Fail fast on wrong credentials or a missing table instead of during recording.
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var probe = new NpgsqlCommand("SELECT 1 FROM measurement LIMIT 1", connection);
        await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AcceptAsync(Reading reading, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pending.Add(reading);
            if (_pending.Count >= _batchSize)
                await InsertPendingAsync(cancellationToken).ConfigureAwait(false);
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
            await InsertPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
            _gate.Dispose();
        }
    }

    private async Task InsertPendingAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection);

        foreach (var reading in _pending)
        {
            var command = new NpgsqlBatchCommand(InsertStatement);
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = reading.ChannelKey });
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = reading.TakenAtUtc });
            command.Parameters.Add(new NpgsqlParameter<double> { TypedValue = reading.Value });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = reading.Quality.ToString().ToLowerInvariant() });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _pending.Clear();
    }
}
