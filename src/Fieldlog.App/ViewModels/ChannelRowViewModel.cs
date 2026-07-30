using System.Globalization;
using Fieldlog.App.Infrastructure;
using Fieldlog.Core.Acquisition;
using Fieldlog.Core.Channels;
using Fieldlog.Core.Localization;

namespace Fieldlog.App.ViewModels;

/// <summary>
/// One row in the channel table: selection state, chosen interval and the most recent value.
/// </summary>
public sealed class ChannelRowViewModel : ObservableObject
{
    private bool _selected = true;
    private int _intervalSeconds;
    private string _lastValue = "—";
    private bool _editable = true;

    public ChannelRowViewModel(ChannelDefinition definition)
    {
        Definition = definition;
        _intervalSeconds = definition.DefaultIntervalSeconds;
    }

    /// <summary>The underlying catalog entry.</summary>
    public ChannelDefinition Definition { get; }

    /// <summary>Localized display name including the unit, e.g. "Line pressure [bar]".</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(Definition.Unit)
            ? Strings.ForChannel(Definition.Key)
            : $"{Strings.ForChannel(Definition.Key)} [{Definition.Unit}]";

    /// <summary>Whether this channel takes part in the next recording.</summary>
    public bool Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    /// <summary>Sampling interval in seconds, clamped to the catalog's allowed range.</summary>
    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set => SetField(ref _intervalSeconds,
            Math.Clamp(value, Definition.MinimumIntervalSeconds, Definition.MaximumIntervalSeconds));
    }

    /// <summary>Formatted most recent sample, for display only.</summary>
    public string LastValue
    {
        get => _lastValue;
        private set => SetField(ref _lastValue, value);
    }

    /// <summary>False while a recording session locks the row against edits.</summary>
    public bool Editable
    {
        get => _editable;
        set => SetField(ref _editable, value);
    }

    /// <summary>Builds the subscription handed to the acquisition engine.</summary>
    public ChannelSubscription ToSubscription() =>
        new(Definition, TimeSpan.FromSeconds(IntervalSeconds));

    /// <summary>Pushes a fresh sample into the row.</summary>
    public void ShowReading(Reading reading)
    {
        LastValue = reading.IsUsable
            ? reading.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : "✕";
    }

    /// <summary>Clears the value display, e.g. after disconnecting.</summary>
    public void ResetDisplay() => LastValue = "—";
}
