using Microsoft.Extensions.Time.Testing;
using Fieldlog.Core.Acquisition;
using Fieldlog.Core.Channels;
using Xunit;

namespace Fieldlog.Core.Tests;

public class SimulatedSourceTests
{
    private static ChannelDefinition WaveChannel(double floor = 0, double ceiling = 100) => new()
    {
        Key = "wave",
        NodeId = "sim",
        Simulation = new SimulationProfile
        {
            Floor = floor,
            Ceiling = ceiling,
            PeriodSeconds = 60,
            Shape = SimulationShape.Wave
        }
    };

    [Fact]
    public async Task Sampling_before_open_throws()
    {
        await using var source = new SimulatedSource();
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.SampleAsync(WaveChannel()));
    }

    [Fact]
    public async Task Values_stay_within_the_configured_bounds()
    {
        var clock = new FakeTimeProvider();
        await using var source = new SimulatedSource(clock);
        await source.OpenAsync();

        var channel = WaveChannel(floor: 10, ceiling: 20);
        for (var i = 0; i < 200; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            var reading = await source.SampleAsync(channel);

            Assert.Equal(ReadingQuality.Good, reading.Quality);
            Assert.InRange(reading.Value, 10, 20);
        }
    }

    [Fact]
    public async Task Two_simulators_with_the_same_clock_produce_identical_waveforms()
    {
        var clock = new FakeTimeProvider();
        await using var first = new SimulatedSource(clock);
        await using var second = new SimulatedSource(clock);
        await first.OpenAsync();
        await second.OpenAsync();

        var channel = WaveChannel();
        clock.Advance(TimeSpan.FromSeconds(17));

        var a = await first.SampleAsync(channel);
        var b = await second.SampleAsync(channel);

        Assert.Equal(a.Value, b.Value, precision: 9);
    }

    [Fact]
    public async Task Integer_channels_yield_whole_numbers()
    {
        var clock = new FakeTimeProvider();
        await using var source = new SimulatedSource(clock);
        await source.OpenAsync();

        var channel = new ChannelDefinition
        {
            Key = "speed",
            NodeId = "sim",
            Kind = ValueKind.Integer,
            Simulation = new SimulationProfile { Floor = 0, Ceiling = 3000, PeriodSeconds = 60 }
        };

        clock.Advance(TimeSpan.FromSeconds(13));
        var reading = await source.SampleAsync(channel);

        Assert.Equal(reading.Value, Math.Round(reading.Value));
    }

    [Fact]
    public async Task Channels_without_a_profile_report_a_flat_zero_line()
    {
        await using var source = new SimulatedSource();
        await source.OpenAsync();

        var channel = new ChannelDefinition { Key = "bare", NodeId = "sim" };
        var reading = await source.SampleAsync(channel);

        Assert.Equal(0d, reading.Value);
        Assert.True(reading.IsUsable);
    }
}
