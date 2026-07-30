namespace Fieldlog.Core.Channels;

/// <summary>
/// How the raw value delivered by a channel should be interpreted.
/// </summary>
public enum ValueKind
{
    /// <summary>Continuous floating point measurement.</summary>
    Real,

    /// <summary>Whole-number measurement; fractional parts are rounded away.</summary>
    Integer,

    /// <summary>Monotonically increasing counter that wraps at its ceiling.</summary>
    Counter
}

/// <summary>
/// Waveform used by the simulator when no physical server is available.
/// </summary>
public enum SimulationShape
{
    /// <summary>Smooth sinusoidal oscillation between floor and ceiling.</summary>
    Wave,

    /// <summary>Linear sawtooth that resets once it reaches the ceiling.</summary>
    Ramp,

    /// <summary>Square-ish alternation between two plateaus.</summary>
    Step,

    /// <summary>Bounded random walk.</summary>
    Drift
}

/// <summary>
/// Parameters that describe the synthetic signal generated for a channel in simulator mode.
/// </summary>
public sealed class SimulationProfile
{
    /// <summary>Lower bound of the generated signal.</summary>
    public double Floor { get; init; }

    /// <summary>Upper bound of the generated signal.</summary>
    public double Ceiling { get; init; } = 100d;

    /// <summary>Duration of one full waveform cycle.</summary>
    public double PeriodSeconds { get; init; } = 120d;

    /// <summary>Amount of random noise layered on top of the waveform, in engineering units.</summary>
    public double Jitter { get; init; }

    /// <summary>Waveform to generate.</summary>
    public SimulationShape Shape { get; init; } = SimulationShape.Wave;

    internal double Span => Ceiling - Floor;

    internal double Midpoint => Floor + (Span / 2d);
}

/// <summary>
/// A single measurement point exposed by an OPC UA server.
/// </summary>
/// <remarks>
/// Channels are declared entirely in configuration. No node identifier is compiled into the
/// application, which keeps the binary independent of any particular machine vendor.
/// </remarks>
public sealed class ChannelDefinition
{
    /// <summary>
    /// Stable, machine-readable identifier. Used as the CSV column value, the database key and
    /// the lookup key for display names. Lower snake case by convention.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The OPC UA node identifier, for example <c>ns=2;s=Plant.Tank1.Temperature</c>.</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>Engineering unit shown in the UI and stored alongside the readings.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>How the raw value should be interpreted.</summary>
    public ValueKind Kind { get; init; } = ValueKind.Real;

    /// <summary>Interval preselected in the user interface.</summary>
    public int DefaultIntervalSeconds { get; init; } = 30;

    /// <summary>Smallest interval the operator may choose.</summary>
    public int MinimumIntervalSeconds { get; init; } = 1;

    /// <summary>Largest interval the operator may choose.</summary>
    public int MaximumIntervalSeconds { get; init; } = 3600;

    /// <summary>Optional simulator parameters. Channels without a profile fall back to a flat line.</summary>
    public SimulationProfile? Simulation { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Key} ({NodeId})";
}
