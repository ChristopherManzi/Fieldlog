using Fieldlog.Core.Channels;

namespace Fieldlog.Core.Acquisition;

/// <summary>
/// Deterministic signal generator used when no OPC UA server is available.
/// </summary>
/// <remarks>
/// Unlike a random-walk mock, the simulator derives every value from the wall clock and the
/// channel's <see cref="SimulationProfile"/>. Two instances started at the same time produce the
/// same curves, which makes UI demos and integration tests reproducible. Optional jitter is
/// layered on top from a seeded generator.
/// </remarks>
public sealed class SimulatedSource : IMeasurementSource
{
    private readonly Random _jitterSource;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, double> _counters = new(StringComparer.Ordinal);
    private DateTimeOffset _epoch;
    private bool _open;

    /// <summary>
    /// Creates a simulator.
    /// </summary>
    /// <param name="clock">Clock abstraction; tests inject a fake to step time manually.</param>
    /// <param name="jitterSeed">Seed for the noise generator, fixed for reproducibility.</param>
    public SimulatedSource(TimeProvider? clock = null, int jitterSeed = 7919)
    {
        _clock = clock ?? TimeProvider.System;
        _jitterSource = new Random(jitterSeed);
    }

    /// <inheritdoc />
    public bool IsOpen => _open;

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!_open)
        {
            _epoch = _clock.GetUtcNow();
            _counters.Clear();
            _open = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _open = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Reading> SampleAsync(ChannelDefinition channel, CancellationToken cancellationToken = default)
    {
        if (!_open)
            throw new InvalidOperationException("The simulator has not been opened.");

        var now = _clock.GetUtcNow();
        var value = channel.Simulation is { } profile
            ? Generate(channel, profile, (now - _epoch).TotalSeconds)
            : 0d;

        if (channel.Kind is ValueKind.Integer or ValueKind.Counter)
            value = Math.Round(value);

        return Task.FromResult(new Reading(channel.Key, now, value, ReadingQuality.Good));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _open = false;
        return ValueTask.CompletedTask;
    }

    private double Generate(ChannelDefinition channel, SimulationProfile profile, double elapsedSeconds)
    {
        var phase = elapsedSeconds / Math.Max(profile.PeriodSeconds, 1d);

        var baseline = profile.Shape switch
        {
            SimulationShape.Wave => profile.Midpoint + (profile.Span / 2d) * Math.Sin(phase * Math.Tau),
            SimulationShape.Ramp => profile.Floor + profile.Span * (phase - Math.Floor(phase)),
            SimulationShape.Step => (phase - Math.Floor(phase)) < 0.5 ? profile.Floor : profile.Ceiling,
            SimulationShape.Drift => NextDrift(channel.Key, profile),
            _ => profile.Midpoint
        };

        if (profile.Jitter > 0d)
            baseline += (_jitterSource.NextDouble() - 0.5) * 2d * profile.Jitter;

        return Math.Clamp(baseline, profile.Floor, profile.Ceiling);
    }

    private double NextDrift(string key, SimulationProfile profile)
    {
        var previous = _counters.TryGetValue(key, out var stored) ? stored : profile.Midpoint;
        var stepBudget = profile.Span * 0.02;
        var next = previous + (_jitterSource.NextDouble() - 0.5) * 2d * stepBudget;
        next = Math.Clamp(next, profile.Floor, profile.Ceiling);
        _counters[key] = next;
        return next;
    }
}
