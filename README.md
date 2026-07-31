# Fieldlog

A Windows desktop logger for wet-process machinery. It samples measurement points from
any OPC UA server on a per-channel schedule and records the readings to a CSV trail
and, optionally, a PostgreSQL time-series store.

The tool was built for pump and mixing equipment (temperatures, flow, rotor speed,
power, pressure), but nothing about a specific machine is compiled in: every
measurement point is declared in a JSON catalog that can be edited without rebuilding
the application.

## What it does

- **One scheduler, not one timer per channel** — the engine keeps a due-time priority
  queue and samples whichever channel is due next; deadlines are anchored to the
  previous deadline, so slow reads never accumulate drift across a long session
- **Failure isolation** — a failed read produces a `bad` sample instead of an
  exception, and a faulting sink (say, the database is briefly down) is reported but
  never interrupts the CSV trail
- **Simulator built in** — enter `sim` as the endpoint and every channel produces a
  deterministic waveform (wave, ramp, step or bounded drift) defined in the catalog,
  which covers demos and UI work without hardware
- **Machine-agnostic catalog** — channels, node ids, units and interval limits live in
  a JSON file, not in code
- **Neutral persistence** — files and database rows always use the channel key; the UI
  language (English/German, following the system locale) never leaks into stored data

## How it works

```text
                       +--------------------+
  config/channels.json |   ChannelCatalog   |
                       +---------+----------+
                                 | subscriptions (channel + interval)
                                 v
   +--------------+     +-------------------+       +---------------+
   | OpcUaSource  |--+   | AcquisitionEngine |--+--> |    CsvSink    |
   +--------------+  +-> |   (single loop,   |  |    +---------------+
   +--------------+  |   |  due-time queue)  |  |    +---------------+
   | SimulatedSrc |--+   +-------------------+  +--> | PostgresSink  |
   +--------------+                                  +---------------+
```

## Requirements

| | |
|---|---|
| OS | Windows 10/11 |
| Runtime | .NET 9 Desktop Runtime (SDK to build) |
| Optional | Docker, only for the PostgreSQL store — CSV logging works without it |

## Getting started

```bash
dotnet build Fieldlog.slnx
```

```bash
dotnet run --project src/Fieldlog.App
```

## Usage

Without hardware: type `sim` into the endpoint field, click **Open**, then **Record**
and pick a CSV target. All eight demo channels start producing data.

Against a real server: enter `opc.tcp://<host>:<port>`, adjust the per-channel
sampling intervals in the table, untick the channels you don't need, and record.

## Output format

```csv
timestamp_utc,channel,value,quality
2024-01-15T12:00:00.000Z,line_pressure,3.5,good
```

ISO-8601 UTC timestamps, invariant number formatting, one row per sample. Rows are
batched in memory and flushed on pause, close and application exit.

## Configuration

Edit `config/channels.json`, or point `FIELDLOG_CHANNELS` at a different file. Each
entry needs a stable `key`, the OPC UA `nodeId`, a `unit` and optionally interval
limits and a simulator profile. Display names for new keys are added in
`src/Fieldlog.Core/Localization/Strings.cs`; untranslated keys fall back to the key
itself.

## Database persistence (optional)

```bash
cp deploy/.env.example deploy/.env    # set FIELDLOG_DB_PASSWORD
```

```bash
docker compose -f deploy/compose.yaml up -d
```

The container initializes the schema from `deploy/initdb/schema.sql` on first start.
Point the app at it before launching:

```text
FIELDLOG_DB_DSN=Host=localhost;Port=5544;Database=fieldlog;Username=fieldlog;Password=<password>
```

If `FIELDLOG_DB_DSN` is unset, Fieldlog records to CSV only.

## Development

```bash
dotnet test Fieldlog.slnx
```

The suite covers catalog validation, the simulator, the CSV sink and the acquisition
engine including sink-fault isolation. CI runs the same on every push via GitHub
Actions.

**Layout:**

| Path | Contents |
|------|----------|
| `src/Fieldlog.Core` | Acquisition engine, sources, sinks, catalog, localization — no UI dependencies |
| `src/Fieldlog.App` | WPF front-end (MVVM) |
| `tests/Fieldlog.Core.Tests` | xUnit suite for the core library |
| `config/channels.json` | Channel catalog (keys, node ids, units, intervals, simulator profiles) |
| `deploy/` | PostgreSQL compose file and schema |

## License

MIT — see [LICENSE](LICENSE).
