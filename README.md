# Fieldlog

Fieldlog is a Windows desktop logger for wet-process machinery. It samples measurement
points from any OPC UA server on a per-channel schedule and records the readings to a CSV
trail and, optionally, a PostgreSQL time-series store.

The tool was built for pump and mixing equipment (temperatures, flow, rotor speed, power,
pressure), but nothing about a specific machine is compiled in: every measurement point is
declared in a JSON catalog that can be edited without rebuilding the application.

## How it works

```
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

- **One scheduler, not one timer per channel.** The engine keeps a due-time priority queue
  and samples whichever channel is due next. Deadlines are anchored to the previous
  deadline, so slow reads never accumulate drift across a long recording session.
- **Failure isolation.** A failed read produces a `bad` sample instead of an exception, and
  a faulting sink (say, the database is briefly down) is reported but never interrupts the
  CSV trail.
- **Simulator built in.** Enter `sim` as the endpoint and every channel produces a
  deterministic waveform (wave, ramp, step or bounded drift) defined in the catalog —
  ideal for demos and UI work without hardware.

## Projects

| Path | Purpose |
|------|---------|
| `src/Fieldlog.Core` | Acquisition engine, sources, sinks, catalog, localization — no UI dependencies |
| `src/Fieldlog.App` | WPF front-end (MVVM) |
| `tests/Fieldlog.Core.Tests` | xUnit suite for the core library |
| `config/channels.json` | Channel catalog (keys, node ids, units, intervals, simulator profiles) |
| `deploy/` | PostgreSQL compose file and schema |

## Requirements

- Windows 10/11 with the .NET 9 Desktop Runtime (SDK to build)
- Docker (only if you want the PostgreSQL store; CSV logging works without it)

## Getting started

Build and launch:

```bash
dotnet build Fieldlog.slnx
dotnet run --project src/Fieldlog.App
```

Try it without hardware: type `sim` into the endpoint field, click **Open**, then
**Record** and pick a CSV target. All eight demo channels start producing data.

Against a real server: enter `opc.tcp://<host>:<port>`, adjust the per-channel sampling
intervals in the table, untick channels you don't need, and record.

## Database persistence (optional)

```bash
cp deploy/.env.example deploy/.env    # set FIELDLOG_DB_PASSWORD
docker compose -f deploy/compose.yaml up -d
```

The container initializes the schema from `deploy/initdb/schema.sql` on first start.
Then point the app at it before launching:

```
FIELDLOG_DB_DSN=Host=localhost;Port=5544;Database=fieldlog;Username=fieldlog;Password=<password>
```

If `FIELDLOG_DB_DSN` is unset, Fieldlog records to CSV only.

## Adapting to your machine

Edit `config/channels.json` (or point `FIELDLOG_CHANNELS` at a different file). Each entry
needs a stable `key`, the OPC UA `nodeId`, a `unit` and optionally interval limits and a
simulator profile. Display names for new keys are added in
`src/Fieldlog.Core/Localization/Strings.cs`; untranslated keys fall back to the key itself.

Persisted data always uses the neutral channel key — the UI language (English/German,
following the system locale) never leaks into files or the database.

## CSV format

```
timestamp_utc,channel,value,quality
2024-01-15T12:00:00.000Z,line_pressure,3.5,good
```

ISO-8601 UTC timestamps, invariant number formatting, one row per sample. Rows are
batched in memory and flushed on pause, close and application exit.

## Tests

```bash
dotnet test Fieldlog.slnx
```

The suite covers catalog validation, the simulator, the CSV sink and the acquisition
engine (including sink-fault isolation). CI runs the same on every push via GitHub Actions.

## License

MIT — see [LICENSE](LICENSE).
