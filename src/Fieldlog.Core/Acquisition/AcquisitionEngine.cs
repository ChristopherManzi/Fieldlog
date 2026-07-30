using Fieldlog.Core.Channels;
using Fieldlog.Core.Sinks;

namespace Fieldlog.Core.Acquisition;

/// <summary>
/// A channel together with the sampling interval chosen by the operator.
/// </summary>
/// <param name="Channel">The channel to sample.</param>
/// <param name="Interval">Time between two consecutive samples.</param>
public sealed record ChannelSubscription(ChannelDefinition Channel, TimeSpan Interval);

/// <summary>
/// Drives periodic sampling of subscribed channels and fans readings out to the registered sinks.
/// </summary>
/// <remarks>
/// The engine runs a single background loop with a due-time priority queue instead of one timer
/// per channel. That keeps resource usage flat regardless of channel count and guarantees that
/// samples for the same source are taken sequentially — OPC UA sessions are not re-entrant.
/// </remarks>
public sealed class AcquisitionEngine : IAsyncDisposable
{
    private readonly IMeasurementSource _source;
    private readonly List<IReadingSink> _sinks = new();
    private readonly TimeProvider _clock;

    private CancellationTokenSource? _stopSignal;
    private Task? _loop;

    /// <summary>Raised for every sample, including failed ones, on a thread-pool thread.</summary>
    public event Action<Reading>? ReadingTaken;

    /// <summary>Raised when a sink rejects a reading, carrying the sink name and the error.</summary>
    public event Action<string, Exception>? SinkFaulted;

    public AcquisitionEngine(IMeasurementSource source, TimeProvider? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>True while the sampling loop is running.</summary>
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>
    /// Registers a sink that will receive every usable reading. Must be called before
    /// <see cref="StartAsync"/>.
    /// </summary>
    public void AttachSink(IReadingSink sink)
    {
        if (IsRunning)
            throw new InvalidOperationException("Sinks cannot be attached while the engine is running.");

        _sinks.Add(sink ?? throw new ArgumentNullException(nameof(sink)));
    }

    /// <summary>
    /// Starts sampling the given subscriptions until <see cref="StopAsync"/> is called.
    /// </summary>
    /// <exception cref="ArgumentException">The subscription list is empty.</exception>
    /// <exception cref="InvalidOperationException">The engine is already running.</exception>
    public async Task StartAsync(IReadOnlyList<ChannelSubscription> subscriptions, CancellationToken cancellationToken = default)
    {
        if (subscriptions.Count == 0)
            throw new ArgumentException("At least one channel subscription is required.", nameof(subscriptions));

        if (IsRunning)
            throw new InvalidOperationException("The engine is already running.");

        if (!_source.IsOpen)
            await _source.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sink in _sinks)
            await sink.PrepareAsync(cancellationToken).ConfigureAwait(false);

        _stopSignal = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(subscriptions, _stopSignal.Token), CancellationToken.None);
    }

    /// <summary>
    /// Stops the loop, flushes all sinks and leaves the source open for a later restart.
    /// </summary>
    public async Task StopAsync()
    {
        if (_stopSignal is null || _loop is null)
            return;

        _stopSignal.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop honours the stop signal.
        }
        finally
        {
            _stopSignal.Dispose();
            _stopSignal = null;
            _loop = null;
        }

        foreach (var sink in _sinks)
            await sink.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        foreach (var sink in _sinks)
            await sink.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunLoopAsync(IReadOnlyList<ChannelSubscription> subscriptions, CancellationToken stop)
    {
        // Due-time queue: the entry with the earliest deadline is always sampled next.
        var queue = new PriorityQueue<ChannelSubscription, DateTimeOffset>();
        var now = _clock.GetUtcNow();
        foreach (var subscription in subscriptions)
            queue.Enqueue(subscription, now);

        while (!stop.IsCancellationRequested && queue.TryDequeue(out var due, out var deadline))
        {
            var wait = deadline - _clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, _clock, stop).ConfigureAwait(false);

            var reading = await _source.SampleAsync(due.Channel, stop).ConfigureAwait(false);
            ReadingTaken?.Invoke(reading);

            if (reading.IsUsable)
                await PublishAsync(reading, stop).ConfigureAwait(false);

            // Anchor the next deadline to the previous one, not to "now", so slow reads
            // do not accumulate drift over long recording sessions.
            queue.Enqueue(due, deadline + due.Interval);
        }
    }

    private async Task PublishAsync(Reading reading, CancellationToken stop)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.AcceptAsync(reading, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One faulty sink (e.g. database briefly down) must not stop the CSV trail.
                SinkFaulted?.Invoke(sink.Name, ex);
            }
        }
    }
}
