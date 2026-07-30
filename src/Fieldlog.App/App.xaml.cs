using System.IO;
using System.Windows;
using Fieldlog.Core.Channels;

namespace Fieldlog.App;

/// <summary>
/// Application entry point: loads the channel catalog and shows the main window.
/// </summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ChannelCatalog catalog;
        try
        {
            catalog = ChannelCatalog.Load(ResolveCatalogPath());
        }
        catch (Exception ex) when (ex is CatalogFormatException or IOException)
        {
            MessageBox.Show(
                $"The channel catalog could not be loaded:\n{ex.Message}",
                "Fieldlog",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow(catalog);
        MainWindow = window;
        window.Show();
    }

    private static string ResolveCatalogPath()
    {
        // Explicit override first, then the copy deployed next to the executable.
        var overridePath = Environment.GetEnvironmentVariable("FIELDLOG_CHANNELS");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(AppContext.BaseDirectory, "config", "channels.json");
    }
}
