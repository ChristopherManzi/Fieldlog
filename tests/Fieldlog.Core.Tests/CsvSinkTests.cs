using Fieldlog.Core.Acquisition;
using Fieldlog.Core.Sinks;
using Xunit;

namespace Fieldlog.Core.Tests;

public class CsvSinkTests : IDisposable
{
    private readonly string _directory;

    public CsvSinkTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "fieldlog-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string NewFilePath() => Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".csv");

    private static Reading SampleReading(double value = 4.2) =>
        new("line_pressure", new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero), value, ReadingQuality.Good);

    [Fact]
    public async Task Prepare_writes_the_header_once()
    {
        var path = NewFilePath();
        await using (var sink = new CsvSink(path))
        {
            await sink.PrepareAsync();
        }

        await using (var sink = new CsvSink(path))
        {
            await sink.PrepareAsync();
        }

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Single(lines);
        Assert.Equal("timestamp_utc,channel,value,quality", lines[0]);
    }

    [Fact]
    public async Task Readings_survive_dispose_even_below_the_batch_threshold()
    {
        var path = NewFilePath();
        await using (var sink = new CsvSink(path, batchSize: 100))
        {
            await sink.PrepareAsync();
            await sink.AcceptAsync(SampleReading());
        }

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("line_pressure", lines[1]);
    }

    [Fact]
    public async Task Rows_use_invariant_iso_formatting()
    {
        var row = CsvSink.FormatRow(SampleReading(3.5));
        Assert.Equal("2024-01-15T12:00:00.000Z,line_pressure,3.5,good", row);
    }

    [Fact]
    public async Task Batch_threshold_triggers_an_automatic_write()
    {
        var path = NewFilePath();
        await using var sink = new CsvSink(path, batchSize: 2);
        await sink.PrepareAsync();

        await sink.AcceptAsync(SampleReading(1));
        await sink.AcceptAsync(SampleReading(2));

        // Not yet disposed or flushed — the batch alone must have hit the disk.
        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(3, lines.Length);
    }
}
