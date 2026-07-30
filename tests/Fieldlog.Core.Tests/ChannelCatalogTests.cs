using Fieldlog.Core.Channels;
using Xunit;

namespace Fieldlog.Core.Tests;

public class ChannelCatalogTests
{
    private const string MinimalCatalog = """
        {
          "channels": [
            { "key": "line_pressure", "nodeId": "ns=2;s=Plant.Line.DeliveryPressure", "unit": "bar" }
          ]
        }
        """;

    [Fact]
    public void Parse_accepts_a_minimal_catalog()
    {
        var catalog = ChannelCatalog.Parse(MinimalCatalog);

        Assert.Equal(1, catalog.Count);
        var channel = catalog.Require("line_pressure");
        Assert.Equal("bar", channel.Unit);
        Assert.Equal(ValueKind.Real, channel.Kind);
    }

    [Fact]
    public void Parse_rejects_duplicate_keys()
    {
        const string json = """
            {
              "channels": [
                { "key": "a", "nodeId": "ns=2;s=X" },
                { "key": "a", "nodeId": "ns=2;s=Y" }
              ]
            }
            """;

        var ex = Assert.Throws<CatalogFormatException>(() => ChannelCatalog.Parse(json));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void Parse_rejects_empty_documents()
    {
        Assert.Throws<CatalogFormatException>(() => ChannelCatalog.Parse("""{ "channels": [] }"""));
    }

    [Fact]
    public void Parse_rejects_invalid_json()
    {
        Assert.Throws<CatalogFormatException>(() => ChannelCatalog.Parse("not json at all"));
    }

    [Fact]
    public void Parse_rejects_interval_ranges_that_exclude_the_default()
    {
        const string json = """
            {
              "channels": [
                {
                  "key": "a", "nodeId": "ns=2;s=X",
                  "defaultIntervalSeconds": 500,
                  "minimumIntervalSeconds": 1,
                  "maximumIntervalSeconds": 300
                }
              ]
            }
            """;

        Assert.Throws<CatalogFormatException>(() => ChannelCatalog.Parse(json));
    }

    [Fact]
    public void Require_throws_for_unknown_keys()
    {
        var catalog = ChannelCatalog.Parse(MinimalCatalog);
        Assert.Throws<CatalogFormatException>(() => catalog.Require("does_not_exist"));
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_keys()
    {
        var catalog = ChannelCatalog.Parse(MinimalCatalog);
        Assert.False(catalog.TryGet("nope", out _));
    }
}
