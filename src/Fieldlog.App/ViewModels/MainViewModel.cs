using System.Text.RegularExpressions;
using Fieldlog.App.Infrastructure;
using Fieldlog.Core.Acquisition;
using Fieldlog.Core.Channels;
using Fieldlog.Core.Localization;
using Fieldlog.Core.Sinks;

namespace Fieldlog.App.ViewModels;

/// <summary>
/// Application state machine behind the main window.
/// </summary>
/// <remarks>
/// States: Idle → Open → Recording. "Open" establishes the source (real server or simulator);
/// "Record" starts the engine with the currently selected channels. All UI-facing errors are
/// surfaced through <see cref="UserMessage"/> so the view stays free of MessageBox plumbing.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Endpoint keyword that activates the built-in simulator.</summary>
    public const string SimulatorKeyword = "sim";

    [GeneratedRegex(@"\Aopc\.tcp://\S+?(:\d{1,5})?\z", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointPattern();

    private readonly SynchronizationContext _uiContext;
    private readonly Func<string?> _pickCsvPath;

    private IMeasurementSource? _source;
    private AcquisitionEngine? _engine;

    private string _endpoint = string.Empty;
    private string _statusText = Strings.Get("status.idle");
    private string? _userMessage;
    private bool _isOpen;
    private bool _isRecording;

    /// <summary>
    /// Creates the view model.
    /// </summary>
    /// <param name="catalog">Channel catalog to present.</param>
    /// <param name="pickCsvPath">
    /// Callback that asks the operator for a CSV target path; returns null when cancelled.
    /// Injected so the view model stays testable without a real file dialog.
    /// </param>
    public MainViewModel(ChannelCatalog catalog, Func<string?> pickCsvPath)
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _pickCsvPath = pickCsvPath;

        Channels = catalog.Channels.Select(c => new ChannelRowViewModel(c)).ToList();

        OpenCommand = new RelayCommand(() => _ = OpenAsync(), () => !IsOpen);
        CloseCommand = new RelayCommand(() => _ = CloseAsync(), () => IsOpen && !IsRecording);
        RecordCommand = new RelayCommand(() => _ = RecordAsync(), () => IsOpen && !IsRecording);
        PauseCommand = new RelayCommand(() => _ = PauseAsync(), () => IsRecording);
    }

    /// <summary>Rows shown in the channel table.</summary>
    public IReadOnlyList<ChannelRowViewModel> Channels { get; }

    public RelayCommand OpenCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand RecordCommand { get; }
    public RelayCommand PauseCommand { get; }

    /// <summary>Endpoint address as typed by the operator.</summary>
    public string Endpoint
    {
        get => _endpoint;
        set => SetField(ref _endpoint, value);
    }

    /// <summary>One-line status shown at the bottom of the window.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Transient message for the operator; the view renders it as a dialog.</summary>
    public string? UserMessage
    {
        get => _userMessage;
        private set => SetField(ref _userMessage, value);
    }

    /// <summary>True while a source session exists.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetField(ref _isOpen, value))
                RefreshCommands();
        }
    }

    /// <summary>True while the engine is sampling.</summary>
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetField(ref _isRecording, value))
                RefreshCommands();
        }
    }

    /// <summary>
    /// Called by the view when the window is about to close; stops everything gracefully.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_engine is not null)
            await _engine.DisposeAsync();

        if (_source is not null)
            await _source.DisposeAsync();

        _engine = null;
        _source = null;
    }

    internal async Task OpenAsync()
    {
        var address = Endpoint.Trim();
        if (address.Length == 0)
        {
            UserMessage = Strings.Get("error.endpointEmpty");
            return;
        }

        var simulator = address.Equals(SimulatorKeyword, StringComparison.OrdinalIgnoreCase);
        if (!simulator && !EndpointPattern().IsMatch(address))
        {
            UserMessage = Strings.Get("error.endpointFormat");
            return;
        }

        _source = simulator ? new SimulatedSource() : new OpcUaSource(address);
        try
        {
            await _source.OpenAsync();
        }
        catch (SourceUnavailableException ex)
        {
            UserMessage = $"{Strings.Get("error.openFailed")} {ex.Message}";
            await _source.DisposeAsync();
            _source = null;
            return;
        }

        IsOpen = true;
        StatusText = simulator ? Strings.Get("status.simulator") : Strings.Get("status.open");
    }

    internal async Task CloseAsync()
    {
        if (_source is null)
            return;

        await _source.DisposeAsync();
        _source = null;
        IsOpen = false;
        StatusText = Strings.Get("status.idle");

        foreach (var row in Channels)
            row.ResetDisplay();
    }

    internal async Task RecordAsync()
    {
        if (_source is null)
            return;

        var subscriptions = Channels
            .Where(row => row.Selected)
            .Select(row => row.ToSubscription())
            .ToList();

        if (subscriptions.Count == 0)
        {
            UserMessage = Strings.Get("error.noChannels");
            return;
        }

        var csvPath = _pickCsvPath();
        if (csvPath is null)
            return; // Operator cancelled the file dialog — nothing started, nothing to undo.

        _engine = new AcquisitionEngine(_source);
        _engine.AttachSink(new CsvSink(csvPath));

        var dsn = Environment.GetEnvironmentVariable("FIELDLOG_DB_DSN");
        if (!string.IsNullOrWhiteSpace(dsn))
            _engine.AttachSink(new PostgresSink(dsn));

        _engine.ReadingTaken += OnReadingTaken;
        _engine.SinkFaulted += OnSinkFaulted;

        try
        {
            await _engine.StartAsync(subscriptions);
        }
        catch (Exception ex)
        {
            UserMessage = $"{Strings.Get("error.sinkFault")} {ex.Message}";
            await _engine.DisposeAsync();
            _engine = null;
            return;
        }

        foreach (var row in Channels)
            row.Editable = false;

        IsRecording = true;
        StatusText = Strings.Get("status.recording");
    }

    internal async Task PauseAsync()
    {
        if (_engine is null)
            return;

        await _engine.DisposeAsync();
        _engine = null;

        foreach (var row in Channels)
            row.Editable = true;

        IsRecording = false;
        StatusText = Strings.Get("status.open");
    }

    /// <summary>Clears the pending user message after the view displayed it.</summary>
    public void AcknowledgeMessage() => UserMessage = null;

    private void OnReadingTaken(Reading reading)
    {
        _uiContext.Post(_ =>
        {
            var row = Channels.FirstOrDefault(r => r.Definition.Key == reading.ChannelKey);
            row?.ShowReading(reading);
        }, null);
    }

    private void OnSinkFaulted(string sinkName, Exception error)
    {
        _uiContext.Post(_ =>
            StatusText = $"{Strings.Get("error.sinkFault")} [{sinkName}] {error.Message}", null);
    }

    private void RefreshCommands()
    {
        OpenCommand.Refresh();
        CloseCommand.Refresh();
        RecordCommand.Refresh();
        PauseCommand.Refresh();
    }
}
