using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Fieldlog.App.ViewModels;
using Fieldlog.Core.Channels;
using Fieldlog.Core.Localization;

namespace Fieldlog.App;

/// <summary>
/// Main window. All logic lives in <see cref="MainViewModel"/>; the code-behind only wires
/// localization, the save-file dialog and the close-confirmation prompt.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _shutdownConfirmed;

    public MainWindow(ChannelCatalog catalog)
    {
        InitializeComponent();

        _viewModel = new MainViewModel(catalog, PickCsvPath);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("app.title");
        OpenButton.Content = Strings.Get("action.open");
        CloseButton.Content = Strings.Get("action.close");
        RecordButton.Content = Strings.Get("action.record");
        PauseButton.Content = Strings.Get("action.pause");
        ColumnChannel.Header = Strings.Get("column.channel");
        ColumnInterval.Header = Strings.Get("column.interval");
        ColumnLastValue.Header = Strings.Get("column.lastValue");
        EndpointBox.ToolTip = Strings.Get("endpoint.watermark");
    }

    private static string? PickCsvPath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"fieldlog_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.UserMessage) || _viewModel.UserMessage is null)
            return;

        MessageBox.Show(this, _viewModel.UserMessage, "Fieldlog", MessageBoxButton.OK, MessageBoxImage.Warning);
        _viewModel.AcknowledgeMessage();
    }

    /// <inheritdoc />
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shutdownConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        if (_viewModel.IsRecording)
        {
            var answer = MessageBox.Show(
                this,
                Strings.Get("confirm.exitBody"),
                Strings.Get("confirm.exitTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        // Defer the actual close until the engine flushed its sinks.
        e.Cancel = true;
        await _viewModel.ShutdownAsync();
        _shutdownConfirmed = true;
        Close();
    }
}
