using System.Collections.Concurrent;
using Fieldlog.Core.Acquisition;
using Fieldlog.Core.Channels;
using Fieldlog.Core.Sinks;
using Xunit;

namespace Fieldlog.Core.Tests;

public class AcquisitionEngineTests
{
    private sealed class CollectingSink : IReadingSink
    {
        public ConcurrentQueue<Reading> Accepted { get; } = new();
        public int FlushCalls;

        public string Name => "collector";

        public Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AcceptAsync(Reading reading, CancellationToken cancellationToken = default)
        {
            Accepted.Enqueue(reading);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref FlushCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSink : IReadingSink
    {
        public string Name => "broken";
        public Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcceptAsync(Reading reading, CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ChannelDefinition FastChannel(string key = "fast") => new()
    {
        Key = key,
        NodeId = "sim",
        Simulation = new SimulationProfile { Floor = 0, Ceiling = 1, PeriodSeconds = 10 }
    };

    [Fact]
    public async Task Start_requires_at_least_one_subscription()
    {
        await using var engine = new AcquisitionEngine(new SimulatedSource());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.StartAsync(Array.Empty<ChannelSubscription>()));
    }

    [Fact]
    public async Task Engine_delivers_readings_to_attached_sinks()
    {
        var sink = new CollectingSink();
        await using var engine = new AcquisitionEngine(new SimulatedSource());
        engine.AttachSink(sink);

        await engine.StartAsync(new[]
        {
            new ChannelSubscription(FastChannel(), TimeSpan.FromMilliseconds(20))
        });

        await WaitUntilAsync(() => sink.Accepted.Count >= 3, TimeSpan.FromSeconds(5));
        await engine.StopAsync();

        Assert.True(sink.Accepted.Count >= 3);
        Assert.All(sink.Accepted, r => Assert.Equal("fast", r.ChannelKey));
    }

    [Fact]
    public async Task Stop_flushes_every_sink()
    {
        var sink = new CollectingSink();
        await using var engine = new AcquisitionEngine(new SimulatedSource());
        engine.AttachSink(sink);

        await engine.StartAsync(new[]
        {
            new ChannelSubscription(FastChannel(), TimeSpan.FromMilliseconds(50))
        });
        await engine.StopAsync();

        Assert.True(sink.FlushCalls >= 1);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task A_faulting_sink_does_not_stop_the_others()
    {
        var healthy = new CollectingSink();
        var faults = 0;

        await using var engine = new AcquisitionEngine(new SimulatedSource());
        engine.AttachSink(new ThrowingSink());
        engine.AttachSink(healthy);
        engine.SinkFaulted += (_, _) => Interlocked.Increment(ref faults);

        await engine.StartAsync(new[]
        {
            new ChannelSubscription(FastChannel(), TimeSpan.FromMilliseconds(20))
        });

        await WaitUntilAsync(() => healthy.Accepted.Count >= 2, TimeSpan.FromSeconds(5));
        await engine.StopAsync();

        Assert.True(healthy.Accepted.Count >= 2);
        Assert.True(faults >= 2);
    }

    [Fact]
    public async Task Attaching_a_sink_while_running_is_rejected()
    {
        await using var engine = new AcquisitionEngine(new SimulatedSource());
        engine.AttachSink(new CollectingSink());

        await engine.StartAsync(new[]
        {
            new ChannelSubscription(FastChannel(), TimeSpan.FromMilliseconds(50))
        });

        Assert.Throws<InvalidOperationException>(() => engine.AttachSink(new CollectingSink()));
        await engine.StopAsync();
    }

    [Fact]
    public async Task Multiple_channels_are_sampled_independently()
    {
        var sink = new CollectingSink();
        await using var engine = new AcquisitionEngine(new SimulatedSource());
        engine.AttachSink(sink);

        await engine.StartAsync(new[]
        {
            new ChannelSubscription(FastChannel("one"), TimeSpan.FromMilliseconds(25)),
            new ChannelSubscription(FastChannel("two"), TimeSpan.FromMilliseconds(25))
        });

        await WaitUntilAsync(
            () => sink.Accepted.Count(r => r.ChannelKey == "one") >= 2 &&
                  sink.Accepted.Count(r => r.ChannelKey == "two") >= 2,
            TimeSpan.FromSeconds(5));
        await engine.StopAsync();

        Assert.Contains(sink.Accepted, r => r.ChannelKey == "one");
        Assert.Contains(sink.Accepted, r => r.ChannelKey == "two");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }
}
