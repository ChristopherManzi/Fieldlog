using System.Globalization;

namespace Fieldlog.Core.Localization;

/// <summary>
/// Minimal string table with English and German translations.
/// </summary>
/// <remarks>
/// Display names for channels are looked up by catalog key (<c>channel.&lt;key&gt;</c>); keys
/// without a translation fall back to the key itself, so newly configured channels are usable
/// before anyone translates them. Data files always use the neutral catalog key — the UI
/// language never leaks into persisted data.
/// </remarks>
public static class Strings
{
    private static readonly Dictionary<string, (string En, string De)> Table = new(StringComparer.Ordinal)
    {
        // Window chrome
        ["app.title"] = ("Fieldlog — Process Data Logger", "Fieldlog — Prozessdaten-Logger"),
        ["endpoint.watermark"] = ("opc.tcp://host:port  (or 'sim')", "opc.tcp://Host:Port  (oder 'sim')"),
        ["action.open"] = ("Open", "Öffnen"),
        ["action.close"] = ("Close", "Trennen"),
        ["action.record"] = ("Record", "Aufzeichnen"),
        ["action.pause"] = ("Pause", "Anhalten"),
        ["column.channel"] = ("Channel", "Kanal"),
        ["column.interval"] = ("Interval (s)", "Intervall (s)"),
        ["column.lastValue"] = ("Last value", "Letzter Wert"),
        ["status.idle"] = ("Not connected", "Nicht verbunden"),
        ["status.open"] = ("Connected — ready to record", "Verbunden — bereit zur Aufzeichnung"),
        ["status.recording"] = ("Recording…", "Aufzeichnung läuft…"),
        ["status.simulator"] = ("Simulator active", "Simulator aktiv"),

        // Messages
        ["error.endpointEmpty"] = ("Please enter an endpoint address first.", "Bitte zuerst eine Endpunkt-Adresse eingeben."),
        ["error.endpointFormat"] = ("The address must look like opc.tcp://host:port, or be 'sim' for the simulator.",
                                    "Die Adresse muss die Form opc.tcp://Host:Port haben, oder 'sim' für den Simulator."),
        ["error.openFailed"] = ("The server could not be reached:", "Der Server ist nicht erreichbar:"),
        ["error.noChannels"] = ("Select at least one channel before recording.", "Bitte vor der Aufzeichnung mindestens einen Kanal auswählen."),
        ["error.sinkFault"] = ("A storage backend reported an error:", "Ein Speicherziel hat einen Fehler gemeldet:"),
        ["confirm.exitTitle"] = ("Recording in progress", "Aufzeichnung aktiv"),
        ["confirm.exitBody"] = ("Recording is still running. Stop it and close the application?",
                                "Die Aufzeichnung läuft noch. Anhalten und die Anwendung beenden?"),

        // Channel display names (keys mirror config/channels.json)
        ["channel.ambient_temp"] = ("Ambient temperature", "Umgebungstemperatur"),
        ["channel.feed_water_temp"] = ("Feed water temperature", "Zulaufwasser-Temperatur"),
        ["channel.slurry_temp"] = ("Slurry temperature", "Frischmörtel-Temperatur"),
        ["channel.feed_water_flow"] = ("Feed water flow", "Zulaufwasser-Durchfluss"),
        ["channel.rotor_speed"] = ("Rotor speed", "Rotordrehzahl"),
        ["channel.reversal_counter"] = ("Reversal runtime counter", "Reversierlaufzeit-Zähler"),
        ["channel.drive_power"] = ("Drive power", "Antriebsleistung"),
        ["channel.line_pressure"] = ("Line pressure", "Leitungsdruck")
    };

    /// <summary>
    /// True when the UI should render German. Anything that is not German renders English.
    /// </summary>
    public static bool UseGerman { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a string table entry; unknown ids are returned verbatim so a missing
    /// translation shows up as its id instead of crashing the UI.
    /// </summary>
    public static string Get(string id) =>
        Table.TryGetValue(id, out var pair) ? (UseGerman ? pair.De : pair.En) : id;

    /// <summary>
    /// Display name for a channel, falling back to the raw key.
    /// </summary>
    public static string ForChannel(string channelKey)
    {
        var id = "channel." + channelKey;
        return Table.ContainsKey(id) ? Get(id) : channelKey;
    }
}
