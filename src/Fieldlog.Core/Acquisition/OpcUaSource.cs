using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Fieldlog.Core.Channels;

namespace Fieldlog.Core.Acquisition;

/// <summary>
/// Measurement source backed by a live OPC UA client session.
/// </summary>
public sealed class OpcUaSource : IMeasurementSource
{
    private readonly string _endpointUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISession? _session;

    /// <summary>
    /// Creates a source for the given endpoint, e.g. <c>opc.tcp://&lt;host&gt;:&lt;port&gt;</c>.
    /// </summary>
    public OpcUaSource(string endpointUrl)
    {
        _endpointUrl = endpointUrl ?? throw new ArgumentNullException(nameof(endpointUrl));
    }

    /// <inheritdoc />
    public bool IsOpen => _session is { Connected: true };

    /// <inheritdoc />
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsOpen)
                return;

            var config = await BuildClientConfigurationAsync(cancellationToken).ConfigureAwait(false);

            // Telemetry is intentionally not wired up; null selects the library default.
            var endpoint = await CoreClientUtils.SelectEndpointAsync(
                config, _endpointUrl, false, 15_000, null!, cancellationToken).ConfigureAwait(false);
            var configured = new ConfiguredEndpoint(
                collection: null,
                description: endpoint,
                configuration: EndpointConfiguration.Create(config));

            // The ISessionFactory route requires the telemetry plumbing this app does not use;
            // the static factory remains fully supported for plain client sessions.
#pragma warning disable CS0618
            _session = await Session.CreateAsync(
                config,
                (ReverseConnectManager?)null,
                configured,
                false,               // updateBeforeConnect
                false,               // checkDomain
                "Fieldlog acquisition",
                120_000,             // session timeout (ms)
                null,                // anonymous identity
                null,                // default locales
                cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
        }
        catch (Exception ex) when (ex is ServiceResultException or UriFormatException)
        {
            throw new SourceUnavailableException($"Could not reach '{_endpointUrl}'.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is null)
                return;

            try
            {
                await _session.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _session.Dispose();
                _session = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Reading> SampleAsync(ChannelDefinition channel, CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null || !session.Connected)
            throw new InvalidOperationException("No OPC UA session is open.");

        var stamp = DateTimeOffset.UtcNow;
        try
        {
            var request = new ReadValueIdCollection
            {
                new ReadValueId { NodeId = NodeId.Parse(channel.NodeId), AttributeId = Attributes.Value }
            };

            var response = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Source,
                nodesToRead: request,
                ct: cancellationToken).ConfigureAwait(false);

            var result = response.Results.FirstOrDefault();
            if (result is null || !StatusCode.IsGood(result.StatusCode))
                return Reading.Failed(channel.Key, stamp);

            if (!TryCoerce(result.Value, out var value))
                return Reading.Failed(channel.Key, stamp);

            var quality = StatusCode.IsUncertain(result.StatusCode)
                ? ReadingQuality.Uncertain
                : ReadingQuality.Good;

            var sourceStamp = result.SourceTimestamp != DateTime.MinValue
                ? new DateTimeOffset(result.SourceTimestamp, TimeSpan.Zero)
                : stamp;

            return new Reading(channel.Key, sourceStamp, value, quality);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A single failed read must not abort acquisition; the engine records the gap.
            return Reading.Failed(channel.Key, stamp);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private static async Task<ApplicationConfiguration> BuildClientConfigurationAsync(CancellationToken cancellationToken)
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = "Fieldlog",
            ApplicationUri = $"urn:{Utils.GetHostName()}:fieldlog",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                // Plant-floor servers frequently present self-signed certificates.
                AutoAcceptUntrustedCertificates = true
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 20_000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 120_000 },
            TransportConfigurations = new TransportConfigurationCollection()
        };

        await config.ValidateAsync(ApplicationType.Client, cancellationToken).ConfigureAwait(false);
        return config;
    }

    private static bool TryCoerce(object? raw, out double value)
    {
        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int or uint or long or ulong or short or ushort or byte or sbyte or decimal:
                value = Convert.ToDouble(raw);
                return true;
            case bool b:
                value = b ? 1d : 0d;
                return true;
            default:
                value = double.NaN;
                return false;
        }
    }
}
